using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;

namespace Host.ImportExport;

public sealed record SpreadsheetData(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);

public static class SpreadsheetFiles
{
    public const long MaxImportBytes = 5 * 1024 * 1024;
    public const int MaxImportRows = 1000;
    private const int MaxColumns = 100;

    public static SpreadsheetData Read(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ReadCsv(stream),
            ".xlsx" => ReadExcel(stream),
            _ => throw new InvalidDataException("Upload a .csv or .xlsx file.")
        };
    }

    public static byte[] WriteCsv(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var output = new StringBuilder();
        AppendCsvRow(output, headers);
        foreach (var row in rows) AppendCsvRow(output, row);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(output.ToString());
    }

    public static byte[] WriteExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var col = 0; col < headers.Count; col++)
        {
            sheet.Cell(1, col + 1).SetValue(headers[col]);
            sheet.Cell(1, col + 1).Style.Font.Bold = true;
        }

        for (var row = 0; row < rows.Count; row++)
        for (var col = 0; col < headers.Count; col++)
            sheet.Cell(row + 2, col + 1).SetValue(col < rows[row].Length ? rows[row][col] : "");

        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, headers.Count).AdjustToContents(10, 50);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static SpreadsheetData ReadCsv(Stream stream)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8, detectEncoding: true, leaveOpen: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()?.Select(h => h.Trim()).ToArray()
            ?? throw new InvalidDataException("The file is empty.");
        if (headers.Length > MaxColumns) throw new InvalidDataException($"A file can have at most {MaxColumns} columns.");

        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            if (fields.Length > MaxColumns) throw new InvalidDataException($"A file can have at most {MaxColumns} columns.");
            if (rows.Count == MaxImportRows) throw new InvalidDataException($"A file can have at most {MaxImportRows} data rows.");
            rows.Add(fields);
        }
        return new SpreadsheetData(headers, rows);
    }

    private static SpreadsheetData ReadExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The workbook has no worksheets.");
        var range = sheet.RangeUsed();
        if (range is null) throw new InvalidDataException("The worksheet is empty.");
        if (range.RowCount() > MaxImportRows + 1)
            throw new InvalidDataException($"A file can have at most {MaxImportRows} data rows.");
        if (range.ColumnCount() > MaxColumns)
            throw new InvalidDataException($"A file can have at most {MaxColumns} columns.");

        var headers = range.FirstRow().Cells(1, range.ColumnCount())
            .Select(cell => cell.GetString().Trim()).ToArray();
        var rows = new List<string[]>();
        foreach (var row in range.RowsUsed().Skip(1))
        {
            var fields = row.Cells(1, range.ColumnCount()).Select(cell =>
            {
                if (cell.HasFormula) throw new InvalidDataException("Import worksheets cannot contain formulas.");
                if (cell.DataType == XLDataType.DateTime)
                    return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (cell.DataType == XLDataType.Number)
                    return cell.GetDouble().ToString("G", CultureInfo.InvariantCulture);
                return cell.GetString();
            }).ToArray();
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(fields);
        }
        return new SpreadsheetData(headers, rows);
    }

    private static void AppendCsvRow(StringBuilder output, IEnumerable<string> values)
    {
        output.AppendJoin(',', values.Select(value => CsvCell(value)));
        output.Append("\r\n");
    }

    private static string CsvCell(string? value)
    {
        value ??= "";
        // Prefix formula-leading text so spreadsheet apps treat exported text as text.
        if (value.TrimStart() is ['=', ..] or ['+', ..] or ['-', ..] or ['@', ..]) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
