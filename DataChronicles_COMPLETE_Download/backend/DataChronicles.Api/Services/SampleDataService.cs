using ClosedXML.Excel;

namespace DataChronicles.Api.Services;

/// <summary>Generates a sample input workbook mirroring the reference test_data_50 file.</summary>
public static class SampleDataService
{
    private static readonly string[] Descriptions =
    {
        "HOLD, Cond was Waiting adapter_host: P4_SCHD cpuid: USTRY1METV0SSDL",
        "Delayed : USTRY1METV0SSDL#BPASD_INFOBATCH1.BPAS_ALL_ACCORD_LOAD State: HOLD",
        "Delayed : USTRY1METV0SSDL#BIBWD_LIFEDW.BIBW_LIFEDW_DAILY_VARI_FTP State: HOLD",
        "Issue Description: F6500GRP- Job abended in step SORP0020 with U0200.BI quarterly guideline data load",
        "Delayed : MSCLP11#BICN_MONTHU_464.BICN_ADG_AGNT_LOAD State: HOLD, Cond was Waiting",
        "Entered By:AUTO, INFO MSG:PROD JOB DOWN: N090 E3469H5P upstream feed unavailable from vendor",
        "Entered By:AUTO, INFO MSG:PROD JOB DOWN FTP server connectivity timeout to remote host",
        "Entered By:AUTO, INFO MSG: database lock contention detected on shared resource table",
        "Data load failed: duplicate records found, mismatch in daily reconciliation file",
        "Server down: network unreachable, connection refused on transfer node"
    };

    public static byte[] Generate(int rows = 50)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell(1, 1).Value = "Description";
        ws.Cell(1, 2).Value = "Application Name";
        ws.Cell(1, 3).Value = "Incident";
        ws.Cell(1, 4).Value = "Job Name";
        ws.Range(1, 1, 1, 4).Style.Font.SetBold();

        for (var i = 0; i < rows; i++)
        {
            var r = i + 2;
            ws.Cell(r, 1).Value = Descriptions[i % Descriptions.Length];
            ws.Cell(r, 2).Value = "Test Application";
            ws.Cell(r, 3).Value = $"INC{123 + i:D5}";
            ws.Cell(r, 4).Value = $"Test_Job_{(i % 12) + 1:D2}";
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
