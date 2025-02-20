using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// 1) ��駤�� Endpoint ��� Key (������ҧ������)
// ��������� appsettings.json ���� Environment Variables 㹡����ҹ��ԧ
string endpoint = "https://js-it.cognitiveservices.azure.com/";
string key = "5YPc1l0j5hAVl36VfCsJ2VjxurNwMusUnjfLIciv9fiGEDvIGziGJQQJ99BBACqBBLyXJ3w3AAALACOGzHjo";

// 2) ���ҧ Credential ��� Client
var credential = new AzureKeyCredential(key);
var client = new DocumentAnalysisClient(new Uri(endpoint), credential);

// 3) ���ҧ WebApplication
var app = builder.Build();

// 4) ���ҧ Endpoint Ẻ Minimal API
//    POST /analyze �Ѻ JSON { "InvoiceUrl": "<URL>" } �����觼��Ѿ�� InvoiceData ��Ѻ�� JSON
app.MapPost("/analyze", async (AnalyzeRequest request) =>
{
    // ��ǹ���Ŵ���ҡ URL
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

    // ���������͡��ô��� DocumentAnalysisClient
    using var stream = new MemoryStream(imageData);
    var operation = await client.AnalyzeDocumentAsync(
        WaitUntil.Completed,
        "JS-IT-INVOICE", // Model ID �ͧ�س (����¹�� Model ID ���١��ͧ)
        stream);
    var analyzeResult = operation.Value;

    // ��Ǩ�ͺ��Ҿ��͡����������
    if (analyzeResult.Documents.Count == 0)
    {
        return Results.BadRequest("No document was detected in the provided image.");
    }

    // ����������������͡�����á
    var doc = analyzeResult.Documents[0];

    // ��ҹ��Ŵ������ͷ���˹�� Custom Model
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

    // ��ҹ��¡�� Items (List)
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

    // ���ҧ�ͺ�硵���Ѿ�� InvoiceData
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

    // �觡�Ѻ���Ѿ���� JSON
    return Results.Ok(invoiceData);
});

// 5) �ѹ�ͻ
app.Run();


// -----------------------------------------------------------------------------
// �ѧ��ѹ������ҹ��Ŵ���������

// ��ҹ��Ŵ����� String
static string? ReadStringField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field) &&
        field.FieldType == DocumentFieldType.String)
    {
        return field.Value.AsString();
    }
    return null;
}

// ��ҹ��Ŵ����� Currency (���� Number) -> �׹����� string ������ѭ�ѡɳ���Шӹǹ
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

// ��ҹ��Ŵ����� Date -> �׹����� string ��ٻẺ "yyyy-MM-dd"
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

// Model ����Ѻ�Ѻ JSON Request
record AnalyzeRequest(string InvoiceUrl);

// Model ����Ѻ�ʴ����Ѿ�� (JSON Response)
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

// Model ����Ѻ��¡�� Items � Invoice
record InvoiceItem(
    string? Description,
    string? Amount,
    double? Confidence
);
