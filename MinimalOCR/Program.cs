using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

string endpoint = "https://js-it.cognitiveservices.azure.com/";
string key = "5YPc1l0j5hAVl36VfCsJ2VjxurNwMusUnjfLIciv9fiGEDvIGziGJQQJ99BBACqBBLyXJ3w3AAALACOGzHjo";

var credential = new AzureKeyCredential(key);
var client = new DocumentAnalysisClient(new Uri(endpoint), credential);

var app = builder.Build();

app.MapPost("/analyze", async (AnalyzeRequest request) =>
{
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

    using var stream = new MemoryStream(imageData);
    var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "JSIT-INVOICE-MODEL", stream);
    var analyzeResult = operation.Value;

    if (analyzeResult.Documents.Count == 0)
        return Results.BadRequest("No document was detected in the provided image.");

    var doc = analyzeResult.Documents[0];

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

    var items = new List<InvoiceItem>();
    if (doc.Fields.TryGetValue("Items", out var itemsField) && itemsField.FieldType == DocumentFieldType.List)
    {
        foreach (var item in itemsField.Value.AsList())
        {
            if (item.FieldType == DocumentFieldType.Dictionary)
            {
                var dict = item.Value.AsDictionary();

                string? productCode = null;
                string? description = null;
                string? quantity = null;
                double? unit = null;
                string? amount = null;
                string? discount = null;
                double? confidence = null;

                if (dict.TryGetValue("ProductCode", out var codeField) && codeField.FieldType == DocumentFieldType.String)
                    productCode = codeField.Value.AsString();

                if (dict.TryGetValue("Description", out var descField) && descField.FieldType == DocumentFieldType.String)
                    description = descField.Value.AsString();

                if (dict.TryGetValue("Quantity", out var qtyField))
                {
                    if (qtyField.FieldType == DocumentFieldType.Double)
                        quantity = qtyField.Value.AsDouble().ToString();
                    else if (qtyField.FieldType == DocumentFieldType.String)
                        quantity = qtyField.Value.AsString();
                }

                if (dict.TryGetValue("Unit", out var unitField))
                {
                    if (unitField.FieldType == DocumentFieldType.Double)
                        unit = unitField.Value.AsDouble();
                    else if (unitField.FieldType == DocumentFieldType.String)
                    {
                        var strVal = unitField.Value.AsString();
                        if (double.TryParse(strVal.Replace(",", "").Replace(" ", ""), out double parsed))
                            unit = parsed;
                    }
                }

                if (dict.TryGetValue("Discount", out var discountField))
                {
                    switch (discountField.FieldType)
                    {
                        case DocumentFieldType.Currency:
                            var currencyVal = discountField.Value.AsCurrency();
                            discount = $"{currencyVal.Symbol}{currencyVal.Amount}";
                            confidence = discountField.Confidence;
                            break;
                        case DocumentFieldType.Double:
                            discount = discountField.Value.AsDouble().ToString();
                            confidence = discountField.Confidence;
                            break;
                        case DocumentFieldType.String:
                            var strVal = discountField.Value.AsString();
                            if (double.TryParse(strVal.Replace(",", "").Replace(" ", ""), out double parsedDiscount))
                                discount = parsedDiscount.ToString();
                            else
                                discount = strVal;
                            confidence = discountField.Confidence;
                            break;
                    }
                }

                if (dict.TryGetValue("Amount", out var amtField))
                {
                    switch (amtField.FieldType)
                    {
                        case DocumentFieldType.Currency:
                            var currencyVal = amtField.Value.AsCurrency();
                            amount = $"{currencyVal.Symbol}{currencyVal.Amount}";
                            confidence = amtField.Confidence;
                            break;
                        case DocumentFieldType.Double:
                            amount = amtField.Value.AsDouble().ToString();
                            confidence = amtField.Confidence;
                            break;
                        case DocumentFieldType.String:
                            amount = amtField.Value.AsString();
                            confidence = amtField.Confidence;
                            break;
                    }
                }

                items.Add(new InvoiceItem(productCode, description, quantity, unit, amount, discount, confidence));
            }
        }
    }

    var invoiceData = new InvoiceData(
        customerAddress,
        customerId,
        customerName,
        customerTaxId,
        dueDate,
        invoiceDate,
        invoiceId,
        totalPrice,
        items,
        subTotal,
        totalDiscount,
        totalTax,
        vendorAddress,
        vendorTaxId,
        vendorName,
        paymentTerm
    );

    return Results.Ok(invoiceData);
});

app.Run();

static string? ReadStringField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field) && field.FieldType == DocumentFieldType.String)
        return field.Value.AsString();
    return null;
}

static string? ReadCurrencyField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field))
    {
        switch (field.FieldType)
        {
            case DocumentFieldType.Currency:
                var currencyVal = field.Value.AsCurrency();
                return $"{currencyVal.Symbol}{currencyVal.Amount}";
            case DocumentFieldType.Double:
                return field.Value.AsDouble().ToString();
            case DocumentFieldType.String:
                return field.Value.AsString();
        }
    }
    return null;
}

static string? ReadDateField(AnalyzedDocument doc, string fieldName)
{
    if (doc.Fields.TryGetValue(fieldName, out var field) && field.FieldType == DocumentFieldType.Date)
        return field.Value.AsDate().ToString("yyyy-MM-dd");
    return null;
}

record AnalyzeRequest(string InvoiceUrl);

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

record InvoiceItem(
    string? ProductCode,
    string? Description,
    string? Quantity,
    double? Unit,
    string? Amount,
    string? Discount,
    double? confidence
);
