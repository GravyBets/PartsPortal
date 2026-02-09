using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;   // OutlookCom
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Controls;
using System.Runtime.Versioning;

namespace MaterialReqAppV3
{
    [SupportedOSPlatform("windows")]
    public partial class BackordersWindow : Window
    {
        private readonly ObservableCollection<Part> _allParts;
        private readonly ICollectionView _partsView;
        private readonly BackorderStore _store = new();
        private readonly ObservableCollection<BackorderLine> _allBackorders = new();
        private readonly ICollectionView _backordersView;
        private readonly object? _settingsObj;

        
        public BackordersWindow(IEnumerable<Part> allParts, object? settingsObj)
        {
            InitializeComponent();

            _settingsObj = settingsObj;

            // Parts list
            _allParts = new ObservableCollection<Part>(allParts ?? Enumerable.Empty<Part>());
            AllPartsListBox.ItemsSource = _allParts;

            _partsView = CollectionViewSource.GetDefaultView(AllPartsListBox.ItemsSource);
            _partsView.Filter = PartsFilter;

            // Backorders list (load from disk)
            foreach (var item in _store.Load().OrderByDescending(x => x.DateAdded))
                _allBackorders.Add(item);

            _backordersView = CollectionViewSource.GetDefaultView(_allBackorders);
            _backordersView.Filter = BackordersFilter;

            BackorderGrid.ItemsSource = _backordersView;

            // Save whenever collection changes (add/remove)
            _allBackorders.CollectionChanged += (_, __) => SaveBackorders();
        }

        private void SaveBackorders()
        {
            // Save newest first
            _store.Save(_allBackorders.OrderByDescending(x => x.DateAdded));
        }

        private bool BackordersFilter(object obj)
        {
            if (obj is not BackorderLine b) return false;

            // Always hide ordered items
            return !b.IsOrdered;
        }        

        private bool PartsFilter(object obj)
        {
            if (obj is not Part p) return false;

            var q = (PartsSearchBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) return true;

            return (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.Material?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void PartsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _partsView.Refresh();
        }

        private void AllPartsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AllPartsListBox.SelectedItem is not Part part) return;

            string desc = part.Description ?? "";
            string mat = part.Material ?? "";

            var dlg = new AddBackorderDialog(desc, mat) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var line in dlg.ResultLines)
                {
                    _allBackorders.Insert(0, new BackorderLine
                    {
                        DateAdded = DateTime.Now,
                        Qty = line.Qty,
                        WorkOrder = (line.WorkOrder ?? "").Trim(),
                        SiteReason = (line.SiteReason ?? "").Trim(),
                        Description = desc,
                        Material = mat
                    });
                }

                _backordersView.Refresh(); // in case ShowOrdered is off, etc.
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = BackorderGrid.SelectedItems.Cast<BackorderLine>().ToList();
            foreach (var item in selected)
                _allBackorders.Remove(item);

            _backordersView.Refresh();
        }

        private async void OrderSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = BackorderGrid.SelectedItems.Cast<BackorderLine>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select one or more backorder rows first.", "Backorders",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validate WO for all selected
            var missingWo = selected.Where(x => string.IsNullOrWhiteSpace(x.WorkOrder)).ToList();
            if (missingWo.Count > 0)
            {
                MessageBox.Show("One or more selected lines are missing a Work Order.\n\n" +
                                "Fix the Work Order and try again.",
                                "Missing Work Order",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            // Output folder (same behavior as MainWindow)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string outDir =
                !string.IsNullOrWhiteSpace(GetSettingString("PdfOutputFolder"))
                    ? GetSettingString("PdfOutputFolder").Trim()
                    : Path.Combine(baseDir, "Output");

            Directory.CreateDirectory(outDir);

            // Template (Issue)
            string templatePath = GetTemplatePath(isReturn: false); // ISSUE
            if (!File.Exists(templatePath))
            {
                MessageBox.Show(
                    "Could not find the ISSUE PDF template:\n\n" + templatePath +
                    "\n\nFix:\n• Ensure the file exists under /Templates in your project\n• Set Copy to Output Directory = Copy if newer",
                    "Template Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }


            // Build summary rows + generate merged PDF
            string outFile;
            int pages;
            List<PrintTabSummary> rows;

            try
            {
                (outFile, pages, rows) = GenerateBackordersMergedPdf(selected, outDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("PDF generation failed:\n\n" + ex.Message, "Backorders",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Email Draft (safer default)
            bool ok;
            try
            {
                ok = await SendPdfEmailOutlookStaAsync(outFile, rows, pages, openDraft: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Email failed:\n\n" + ex.Message, "Backorders",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!ok)
                return;

            // Success -> remove from cart (simple “rolling list” behavior)
            foreach (var line in selected)
                _allBackorders.Remove(line);

            MessageBox.Show("Outlook draft created. Removed selected lines from Backorder Cart.",
                "Backorders", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private (string OutFile, int Pages, List<PrintTabSummary> Rows) GenerateBackordersMergedPdf(
            List<BackorderLine> selected,string outDir)

        {
            // Group by Work Order
            var groups = selected
                .GroupBy(x => x.WorkOrder.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .ToList();

            var rows = new List<PrintTabSummary>();

            using var outDoc = new PdfDocument();

            string issueTemplatePath = GetTemplatePath(isReturn: false);
            if (!File.Exists(issueTemplatePath))
                throw new FileNotFoundException("Issue template not found.", issueTemplatePath);


            foreach (var g in groups)
            {
                string wo = g.Key;

                // Combine duplicate materials for cleaner PDFs (optional but recommended)
                var combined = g
                    .GroupBy(x => (x.Material ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(matGroup =>
                    {
                        var first = matGroup.First();
                        return new PrintPartLine
                        {
                            Qty = matGroup.Sum(z => Math.Max(0, z.Qty)),
                            Description = first.Description ?? "",
                            Material = first.Material ?? ""
                        };
                    })
                    .Where(p => p.Qty > 0)
                    .ToList();

                // Build DocHeader from Site/Reason entries
                string docHeader = BuildDocHeader(g.ToList(), wo);

                // Summary row for email body (one per WO)
                rows.Add(new PrintTabSummary
                {
                    Tab = docHeader,
                    Type = "Issue",
                    RefLabel = "WorkOrder",
                    RefValue = wo,
                    Parts = combined,
                    MissingReason = string.IsNullOrWhiteSpace(docHeader),
                    MissingRef = false
                });

                // Split into pages of 12 lines
                var chunks = Chunk(combined, 12);
                for (int pageIndex = 0; pageIndex < chunks.Count; pageIndex++)
                {
                    var pageLines = chunks[pageIndex];
                    using var loaded = new PdfLoadedDocument(issueTemplatePath);

                    // Header
                    SetPdfTextField(loaded, "Name", GetPdfName());
                    SetPdfTextField(loaded, "Date", DateTime.Now.ToString("MM/dd/yyyy"));
                    SetPdfTextField(loaded, "DocHeader", pageIndex == 0 ? docHeader : $"{docHeader} (cont.)");

                    // Assignment (WO only)
                    SetPdfTextField(loaded, "CostCenter", "");
                    SetPdfTextField(loaded, "Wbs", "");
                    SetPdfTextField(loaded, "WorkOrder", wo);
                    SetPdfTextField(loaded, "WorkOrderUsed", "");

                    // Lines 1..12
                    for (int i = 1; i <= 12; i++)
                    {
                        var line = (i - 1 < pageLines.Count) ? pageLines[i - 1] : null;

                        SetPdfTextField(loaded, $"DESCRIPTION{i}", line?.Description ?? "");
                        SetPdfTextField(loaded, $"MATERIAL{i}", line?.Material ?? "");
                        SetPdfTextField(loaded, $"QUANTITY{i}", line != null ? line.Qty.ToString() : "");
                    }

                    // Issue = 261
                    SetMovementTypeRadioBestEffort(loaded, "261");

                    // Flatten
                    if (loaded.Form != null)
                    {
                        loaded.Form.SetDefaultAppearance(false);
                        loaded.Form.FlattenFields();
                    }

                    // Merge
                    outDoc.ImportPageRange(loaded, 0, loaded.Pages.Count - 1);
                }
            }

            if (outDoc.Pages.Count == 0)
                throw new InvalidOperationException("No pages were added to the PDF.");

            string outFile = Path.Combine(outDir, BuildBackordersPdfFileName(outDoc.Pages.Count));
            using var fs = File.Create(outFile);
            outDoc.Save(fs);

            return (outFile, outDoc.Pages.Count, rows);
        }

        private static List<List<PrintPartLine>> Chunk(List<PrintPartLine> lines, int size)
        {
            var result = new List<List<PrintPartLine>>();
            for (int i = 0; i < lines.Count; i += size)
                result.Add(lines.Skip(i).Take(size).ToList());
            return result;
        }

        private string BuildDocHeader(List<BackorderLine> lines, string wo)
        {
            var reasons = lines
                .Select(x => (x.SiteReason ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (reasons.Count == 0) return $"Backorder - WO {wo}";
            if (reasons.Count == 1) return reasons[0];

            // keep it short for the PDF field
            return $"{reasons[0]} (+{reasons.Count - 1} more)";
        }

        private string BuildBackordersPdfFileName(int pages)
        {
            string who = GetEmailDisplayName();
            string dateToken = DateTime.Now.ToString("yyyy-MM-dd");
            string timeToken = DateTime.Now.ToString("HHmm");
            string safeWho = string.Concat(who.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

            return $"Backorders_Issue_{safeWho}_{dateToken}_{timeToken}_{pages}p.pdf";
        }

        private Task<bool> SendPdfEmailOutlookStaAsync(string pdfPath, List<PrintTabSummary>? rows, int pages, bool openDraft)
        {
            
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    bool ok = SendPdfEmailOutlook(pdfPath, rows, pages, openDraft);
                    tcs.SetResult(ok);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private bool SendPdfEmailOutlook(string pdfPath, List<PrintTabSummary>? rows, int pages, bool openDraft)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            rows ??= new List<PrintTabSummary>();

            string to = (GetSettingString("EmailTo") ?? "").Trim();
            string cc = (GetSettingString("EmailCc") ?? "").Trim();

            const string requiredCc = "smartgridradio@centerpointenergy.com";

            if (string.IsNullOrWhiteSpace(cc))
                cc = requiredCc;
            else if (!cc.Contains(requiredCc, StringComparison.OrdinalIgnoreCase))
                cc = cc + "; " + requiredCc;

            if (string.IsNullOrWhiteSpace(to) && string.IsNullOrWhiteSpace(cc))
            {
                MessageBox.Show("Email To/CC is blank. Add recipients in Settings → Email.",
                    "Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string dateToken = DateTime.Now.ToString("MM/dd/yyyy");
            string subject = $"Backorders Material Issue - {GetEmailDisplayName()} - {dateToken} - {pages} page(s)";
            string body = BuildEmailBody(rows, pages);

            object? outlookApp = null;
            object? mailItem = null;
            object? attachments = null;

            try
            {
                outlookApp = OutlookCom.GetOrStartOutlook();

                mailItem = outlookApp.GetType().InvokeMember(
                    "CreateItem",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    outlookApp,
                    new object[] { 0 });

                if (mailItem == null)
                    throw new InvalidOperationException("Outlook CreateItem returned null.");

                SetComProperty(mailItem, "To", to);
                SetComProperty(mailItem, "CC", cc);
                SetComProperty(mailItem, "Subject", subject);
                SetComProperty(mailItem, "Body", body);

                attachments = GetComProperty(mailItem, "Attachments");
                if (attachments == null)
                    throw new InvalidOperationException("Outlook Attachments collection was null.");

                attachments.GetType().InvokeMember("Add",
                    System.Reflection.BindingFlags.InvokeMethod, null, attachments,
                    new object[] { pdfPath });

                if (openDraft)
                {
                    mailItem.GetType().InvokeMember("Display",
                        System.Reflection.BindingFlags.InvokeMethod, null, mailItem, new object[] { false });
                    return true;
                }
                else
                {
                    mailItem.GetType().InvokeMember("Send",
                        System.Reflection.BindingFlags.InvokeMethod, null, mailItem, null);
                    return true;
                }
            }
            catch (COMException ex)
            {
                MessageBox.Show(
                    "Outlook email failed.\n\n" +
                    $"HRESULT: 0x{ex.HResult:X8}\n" +
                    ex.Message,
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }
            finally
            {
                SafeReleaseComObject(attachments);
                SafeReleaseComObject(mailItem);
                SafeReleaseComObject(outlookApp);
            }
        }

        private string BuildEmailBody(List<PrintTabSummary> rows, int pages)
        {
            rows ??= new List<PrintTabSummary>();

            var lines = new List<string>
    {
        $"Attached is the Backorders Material Issue PDF ({pages} page(s)).",
        "",
        "Summary:"
    };

            foreach (var r in rows)
            {
                string site = (r.Tab ?? "").Trim();
                if (string.IsNullOrWhiteSpace(site)) site = "(blank)";

                string wo = (r.RefValue ?? "").Trim();
                if (string.IsNullOrWhiteSpace(wo)) wo = "(missing)";

                int partCount = 0;
                if (r.Parts != null)
                    partCount = r.Parts.Sum(p => Math.Max(0, p.Qty));

                lines.Add($"- {site} | WorkOrder: {wo} | {partCount} parts");
            }

            lines.Add("");
            lines.Add("Thank you!");
            lines.Add("");
            lines.Add(GetEmailDisplayName());

            return string.Join(Environment.NewLine, lines);
        }

        private static void SetComProperty(object? target, string name, object? value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.SetProperty, null, target, new object?[] { value });
        }

        private static object? GetComProperty(object? target, string name)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.GetProperty, null, target, null);
        }

        private static void SafeReleaseComObject(object? obj)
        {
            try
            {
                if (obj != null && Marshal.IsComObject(obj))
                    Marshal.FinalReleaseComObject(obj);
            }
            catch { }
        }

        private void SetPdfTextField(PdfLoadedDocument doc, string fieldName, string value)
        {
            if (doc.Form == null) return;

            for (int i = 0; i < doc.Form.Fields.Count; i++)
            {
                if (doc.Form.Fields[i] is PdfLoadedTextBoxField tb &&
                    string.Equals(tb.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = value ?? "";
                    return;
                }
            }
        }

        // Best-effort radio select (won’t crash if names differ)
        private void SetMovementTypeRadioBestEffort(PdfLoadedDocument doc, string mtCode)
        {
            if (doc.Form == null) return;

            for (int i = 0; i < doc.Form.Fields.Count; i++)
            {
                if (doc.Form.Fields[i] is PdfLoadedRadioButtonListField rbl)
                {
                    // try select an item whose value/text contains the code
                    foreach (PdfLoadedRadioButtonItem item in rbl.Items)
                    {
                        var v = (item.Value ?? "").Trim();
                        if (v.Contains(mtCode, StringComparison.OrdinalIgnoreCase))
                        {
                            rbl.SelectedValue = item.Value ?? "";
                            return;
                        }
                    }
                }
            }

            // fallback: if your template has a movement text field, fill it
            SetPdfTextField(doc, "MovementType", mtCode);
        }        

        private string GetPdfName()
        {
            // Reuse whatever you store in settings for the Name field
            return (GetSettingString("PdfName")
                ?? GetSettingString("Name")
                ?? GetSettingString("EmployeeName")
                ?? GetSettingString("UserName")
                ?? GetEmailDisplayName()
                ?? Environment.UserName).Trim();
        }

        private string GetEmailDisplayName()
        {
            return (GetSettingString("EmailDisplayName")
                ?? GetSettingString("Name")
                ?? GetSettingString("EmployeeName")
                ?? Environment.UserName).Trim();
        }

        private string GetSettingString(string propName)
        {
            if (_settingsObj == null) return "";

            var t = _settingsObj.GetType();
            var p = t.GetProperty(propName);
            if (p == null) return "";

            return p.GetValue(_settingsObj) as string ?? "";
        }

        private string GetTemplatePath(bool isReturn)
        {
            string fileName = isReturn
                ? "Material_Requisition_Return_Fillable.pdf"
                : "Material_Requisition_Issue_Fillable.pdf";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory; // bin\Debug\net8.0-windows\
            return System.IO.Path.Combine(baseDir, "Templates", fileName);
        }


    }
}
