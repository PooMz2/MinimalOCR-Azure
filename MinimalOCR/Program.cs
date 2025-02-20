using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// 1) ตั้งค่า Endpoint และ Key (ตัวอย่างฮาร์ดโค้ด)
// ควรเก็บไว้ใน appsettings.json หรือ Environment Variables ในการใช้งานจริง
string endpoint = "https://js-it.cognitiveservices.azure.com/";
string key = "5YPc1l0j5hAVl36VfCsJ2VjxurNwMusUnjfLIciv9fiGEDvIGziGJQQJ99BBACqBBLyXJ3w3AAALACOGzHjo";

// 2) สร้าง Credential และ Client
var credential = new AzureKeyCredential(key);
var client = new DocumentAnalysisClient(new Uri(endpoint), credential);

// 3) สร้าง WebApplication
var app = builder.Build();

// 4) สร้าง Endpoint แบบ Minimal API
//    POST /analyze รับ JSON { "InvoiceUrl": "<URL>" } แล้วส่งผลลัพธ์ InvoiceData กลับเป็น JSON
app.MapPost("/analyze", async (AnalyzeRequest request) =>
{
    // ดาวน์โหลดไฟล์จาก URL
    byte[] imageData;
    try
    {
        using var wc = new WebClient();
        imageData = wc.DownloadData(request.InvoiceUrl);
    }
    catch
    {
        return Results.BadRequest("Unable to download image data from the provided URL.");
    }

    // วิเคราะห์เอกสารด้วย DocumentAnalysisClient
    using var stream = new MemoryStream(imageData);
    var operation = await client.AnalyzeDocumentAsync(
        WaitUntil.Completed,
        "JS-IT-INVOICE", // Model ID ของคุณ (เปลี่ยนเป็น Model ID ที่ถูกต้อง)
        stream);
    var analyzeResult = operation.Value;

    // ตรวจสอบว่าพบเอกสารหรือไม่
    if (analyzeResult.Documents.Count == 0)
    {
        return Results.BadRequest("No document was detected in the provided image.");
    }

    // สมมติวิเคราะห์แค่เอกสารใบแรก
    var doc = analyzeResult.Documents[0];

    // อ่านฟิลด์ตามชื่อที่กำหนดใน Custom Model
    string? customerAddress = ReadStringField(doc, "CustomerAddress");
    string? customerId = ReadStringField(doc, "CustomerId");
    string? customerName = ReadStringField(doc, "CustomerName");
    string? customerTaxId = ReadStringField(doc, "CustomerTaxId");

    string? dueDate = ReadDateField(doc, "DueDate");
    string? invoiceDate = ReadDateField(doc, "InvoiceDate");
    string? invoiceId = ReadStringField(doc, "InvoiceId");

    string? totalPrice = ReadCurrencyField(doc, "TotalPrice");
    string? subTotal = ReadCurrencyField(doc, "SubTotal");
    string? totalDiscount = ReadCurrencyField(doc, "TotalDiscount");
    string? totalTax = ReadCurrencyField(doc, "TotalTax");

    string? vendorAddress = ReadStringField(doc, "VendorAddress");
    string? vendorTaxId = ReadStringField(doc, "VendorTaxId");
    string? vendorName = ReadStringField(doc, "VendorName");
    string? paymentTerm = ReadStringField(doc, "PaymentTerm");

    // อ่านรายการ Items (List)
    var items = new List<InvoiceItem>();
    if (doc.Fields.TryGetValue("Items", out var itemsField) &&
        itemsField.FieldType == DocumentFieldType.List)
    {
        foreach (var itemField in itemsField.Value.AsList())
        {
            if (itemField.FieldType == DocumentFieldType.Dictionary)
            {
                var dict = itemField.Value.AsDictionary();

                string? description = null;
                string? amount = null;
                double? confidence = null;

                if (dict.TryGetValue("Description", out var descField) &&
                    descField.FieldType == DocumentFieldType.String)
                {
                    description = descField.Value.AsString();
                }

                if (dict.TryGetValue("Amount", out var amtField) &&
                    amtField.FieldType == DocumentFieldType.Currency)
                {
                    var currencyVal = amtField.Value.AsCurrency();
                    amount = $"{currencyVal.Symbol}{currencyVal.Amount}";
                    confidence = amtField.Confidence;
                }

                items.Add(new InvoiceItem(description, amount, confidence));
            }
        }
    }

    // สร้างออบเจ็กต์ผลลัพธ์ InvoiceData
    var invoiceData = new InvoiceData(
        CustomerAddress: customerAddress,
        CustomerId: customerId,
        CustomerName: customerName,
        CustomerTaxId: customerTaxId,
        DueDate: dueDate,
        InvoiceDate: invoiceDate,
        InvoiceId: invoiceId,
        TotalPrice: totalPrice,
        Items: items,
        SubTotal: subTotal,
        TotalDiscount: totalDiscount,
        TotalTax: totalTax,
        VendorAddress: vendorAddress,
        VendorTaxId: vendorTaxId,
        VendorName: vendorName,
        PaymentTerm: paymentTerm
    );

    // ส่งกลับผลลัพธ์เป็น JSON
    return Results.Ok(invoiceData);
});

// 5) รันแอป
app.Run();


// -----------------------------------------------------------------------------
// ฟังก์ชันช่วยอ่านฟิลด์ตามประเภท

// อ่านฟิลด์ที่เป็น String
static string? ReadStringField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field) &&
        field.FieldType == DocumentFieldType.String)
    {
        return field.Value.AsString();
    }
    return null;
}

// อ่านฟิลด์ที่เป็น Currency (หรือ Number) -> คืนค่าเป็น string ที่มีสัญลักษณ์และจำนวน
static string? ReadCurrencyField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field))
    {
        if (field.FieldType == DocumentFieldType.Currency)
        {
            var currencyVal = field.Value.AsCurrency();
            return $"{currencyVal.Symbol}{currencyVal.Amount}";
        }
    }
    return null;
}

// อ่านฟิลด์ที่เป็น Date -> คืนค่าเป็น string ในรูปแบบ "yyyy-MM-dd"
static string? ReadDateField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field) &&
        field.FieldType == DocumentFieldType.Date)
    {
        return field.Value.AsDate().ToString("yyyy-MM-dd");
    }
    return null;
}

// -----------------------------------------------------------------------------
// Request/Response Models

// Model สำหรับรับ JSON Request
record AnalyzeRequest(string InvoiceUrl);

// Model สำหรับแสดงผลลัพธ์ (JSON Response)
record InvoiceData(
    string? CustomerAddress,
    string? CustomerId,
    string? CustomerName,
    string? CustomerTaxId,
    string? DueDate,
    string? InvoiceDate,
    string? InvoiceId,
    string? TotalPrice,
    List<InvoiceItem> Items,
    string? SubTotal,
    string? TotalDiscount,
    string? TotalTax,
    string? VendorAddress,
    string? VendorTaxId,
    string? VendorName,
    string? PaymentTerm
);

// Model สำหรับรายการ Items ใน Invoice
record InvoiceItem(
    string? Description,
    string? Amount,
    double? Confidence
);
