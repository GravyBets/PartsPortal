using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;   // OutlookCom
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        private readonly UserSettings? _settings;
        private bool _updatingHeader;
        private bool _updatingSelectAll;


        //CONSTRUCTOR
        public BackordersWindow(IEnumerable<Part> allParts, UserSettings? settings)
        {
            InitializeComponent();

            _settings = settings;

            // Parts list
            _allParts = new ObservableCollection<Part>(allParts ?? Enumerable.Empty<Part>());
            AllPartsListBox.ItemsSource = _allParts;

            _partsView = CollectionViewSource.GetDefaultView(AllPartsListBox.ItemsSource);
            _partsView.Filter = PartsFilter;

            // Backorders list (load from disk)
            foreach (var item in _store.Load().OrderByDescending(x => x.DateAdded))
            {
                HookBackorderLine(item);
                _allBackorders.Add(item);
            }

            _backordersView = CollectionViewSource.GetDefaultView(_allBackorders);
            _backordersView.Filter = BackordersFilter;

            BackorderGrid.ItemsSource = _backordersView;
            Loaded += (_, __) =>
                Dispatcher.BeginInvoke(UpdateSelectAllHeader, System.Windows.Threading.DispatcherPriority.Loaded);

            UpdateActionButtonsText();

            BackorderGrid.SelectionChanged += (_, __) =>
            {
                EditSelectedButton.Visibility =
                    BackorderGrid.SelectedItem is BackorderLine
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // ✅ Keep action buttons in sync with row selection fallback
                UpdateActionButtonsText();
            };



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

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = _backordersView.Cast<object>()
                .OfType<BackorderLine>()
                .Where(x => x.IsChecked)
                .ToList();

            foreach (var item in toRemove)
                _allBackorders.Remove(item);

            _backordersView.Refresh();
            // ✅ CollectionChanged already saves on remove

            UpdateSelectAllHeader();
            UpdateActionButtonsText();

        }


        private (string OutFile, int Pages, List<PrintTabSummary> Rows) GenerateBackordersMergedPdf(List<BackorderLine> selected, string outDir, string issueTemplatePath)
        {
            // ✅ use the parameter; don't redeclare it
            if (string.IsNullOrWhiteSpace(issueTemplatePath))
                throw new ArgumentException("issueTemplatePath is blank.", nameof(issueTemplatePath));

            if (!File.Exists(issueTemplatePath))
                throw new FileNotFoundException("Issue PDF template not found.", issueTemplatePath);

            // Group by Work Order
            var groups = selected
                .GroupBy(x => (x.WorkOrder ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .ToList();

            var rows = new List<PrintTabSummary>();
            using var outDoc = new PdfDocument();

            foreach (var g in groups)
            {
                string wo = g.Key;

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

                var groupLines = g.ToList();
                bool missingReason = groupLines.All(x => string.IsNullOrWhiteSpace(x.SiteReason));
                string docHeader = BuildDocHeader(groupLines, wo);


                rows.Add(new PrintTabSummary
                {
                    Tab = docHeader,
                    Type = "Issue",
                    RefLabel = "WorkOrder",
                    RefValue = wo,
                    Parts = combined,
                    MissingReason = missingReason,
                    MissingRef = string.IsNullOrWhiteSpace(wo)
                });


                var chunks = Chunk(combined, 12);
                for (int pageIndex = 0; pageIndex < chunks.Count; pageIndex++)
                {
                    var pageLines = chunks[pageIndex];
                    using var loaded = new PdfLoadedDocument(issueTemplatePath);

                    SetPdfTextField(loaded, "Name", GetPdfName());
                    SetPdfTextField(loaded, "Date", DateTime.Now.ToString("MM/dd/yyyy"));
                    SetPdfTextField(loaded, "DocHeader", pageIndex == 0 ? docHeader : $"{docHeader} (cont.)");

                    SetPdfTextField(loaded, "CostCenter", "");
                    SetPdfTextField(loaded, "Wbs", "");
                    SetPdfTextField(loaded, "WorkOrder", wo);
                    SetPdfTextField(loaded, "WorkOrderUsed", "");

                    for (int i = 1; i <= 12; i++)
                    {
                        var line = (i - 1 < pageLines.Count) ? pageLines[i - 1] : null;

                        SetPdfTextField(loaded, $"DESCRIPTION{i}", line?.Description ?? "");
                        SetPdfTextField(loaded, $"MATERIAL{i}", line?.Material ?? "");
                        SetPdfTextField(loaded, $"QUANTITY{i}", line != null ? line.Qty.ToString() : "");
                    }

                    SetMovementTypeRadio(loaded, "261");

                    if (loaded.Form != null)
                    {
                        loaded.Form.SetDefaultAppearance(false);
                        loaded.Form.FlattenFields();
                    }

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

            thread.IsBackground = false;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private bool SendPdfEmailOutlook(string pdfPath, List<PrintTabSummary>? rows, int pages, bool openDraft)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            rows ??= new List<PrintTabSummary>();

            string to = (_settings?.EmailTo ?? "").Trim();
            string cc = (_settings?.EmailCc ?? "").Trim();


            const string requiredCc = "smartgridradio@centerpointenergy.com";

            if (string.IsNullOrWhiteSpace(cc))
                cc = requiredCc;
            else if (!cc.Contains(requiredCc, StringComparison.OrdinalIgnoreCase))
                cc = cc + "; " + requiredCc;

            if (string.IsNullOrWhiteSpace(to) && string.IsNullOrWhiteSpace(cc))
            {
                // ✅ Don't show MessageBox on STA worker thread
                return false;
            }

            string dateToken = DateTime.Now.ToString("MM/dd/yyyy");
            string who = GetEmailDisplayName();
            string pageWord = pages == 1 ? "page" : "pages";
            string subject = $"Backorders Material Issue - {who} - {dateToken} - {pages} {pageWord}";


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

                attachments.GetType().InvokeMember(
                    "Add",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    attachments,
                    new object?[] { pdfPath, Type.Missing, Type.Missing, Type.Missing }
                );


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
                // ✅ Don't show MessageBox on STA worker thread
                throw new InvalidOperationException(
                    $"Outlook email failed (HRESULT: 0x{ex.HResult:X8}). {ex.Message}", ex);
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
        $"Attached is the Backorders Material Issue PDF ({pages} {(pages == 1 ? "page" : "pages")}).",

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

                lines.Add($"- {site} | WorkOrder: {wo} | {partCount} {(partCount == 1 ? "part" : "parts")}");

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

        private static void SetMovementTypeRadio(PdfLoadedDocument loaded, string movementCode)
        {
            if (loaded?.Form?.Fields == null) return;
            if (string.IsNullOrWhiteSpace(movementCode)) return;

            // Your templates might have either name
            string[] possibleNames = { "MovementType", "Movement Type" };

            foreach (PdfField f in loaded.Form.Fields)
            {
                if (!possibleNames.Any(n => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // MovementType is a RADIO button list
                if (f is PdfLoadedRadioButtonListField rb)
                {
                    // Most reliable: select by matching OptionValue (e.g., "261", "202", "962")
                    foreach (PdfLoadedRadioButtonItem item in rb.Items)
                    {
                        if (string.Equals(item.OptionValue, movementCode, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Selected = true;
                            return;
                        }
                    }

                    // Fallback
                    rb.SelectedValue = movementCode;
                    return;
                }
            }
        }

        private string GetPdfName()
        {
            string name = (_settings?.Name ?? "").Trim();
            string emp = (_settings?.EmployeeId ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(emp))
                return $"{name} ({emp})";

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            // fallback
            return Environment.UserName;
        }

        private string GetEmailDisplayName()
        {
            // UserSettings has Name + EmployeeId (no EmailDisplayName / EmployeeName)
            string name = (_settings?.Name ?? "").Trim();
            string emp = (_settings?.EmployeeId ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(emp))
                return $"{name} ({emp})";

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return Environment.UserName;
        }

        private string GetTemplatePath(bool isReturn)
        {
            string fileName = isReturn
                ? "Material_Requisition_Return_Fillable.pdf"
                : "Material_Requisition_Issue_Fillable.pdf";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory; // bin\Debug\net8.0-windows\
            return System.IO.Path.Combine(baseDir, "Templates", fileName);
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
            {
                MessageBox.Show("Settings not loaded.", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var win = new SettingsWindow(_settings)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();
        }

        private void AllPartsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AddBackorderFromSelectedPart();
        }
        private void AddToBackorder_Click(object sender, RoutedEventArgs e)
        {
            AddBackorderFromSelectedPart();
        }
        private void AddBackorderFromSelectedPart()
        {
            if (AllPartsListBox.SelectedItem is not Part part)
            {
                MessageBox.Show("Select a part on the left first (or double-click it).",
                    "Add to Backorder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string desc = (part.Description ?? "").Trim();
            string mat = (part.Material ?? "").Trim();

            var dlg = new AddBackorderDialog(desc, mat)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dlg.ShowDialog() != true) return;
            if (dlg.ResultLines == null || dlg.ResultLines.Count == 0) return;

            foreach (var line in dlg.ResultLines)
            {
                var newLine = new BackorderLine
                {
                    DateAdded = DateTime.Now,
                    Qty = line.Qty,
                    WorkOrder = (line.WorkOrder ?? "").Trim(),
                    SiteReason = (line.SiteReason ?? "").Trim(),
                    Description = desc,
                    Material = mat
                };

                HookBackorderLine(newLine);
                _allBackorders.Insert(0, newLine);
                UpdateSelectAllHeader();

            }

            _backordersView.Refresh();
            // ✅ CollectionChanged already saves on insert

        }

        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (BackorderGrid.SelectedItem is not BackorderLine selected)
                return;

            string desc = (selected.Description ?? "").Trim();
            string mat = (selected.Material ?? "").Trim();

            // Prefill from selected row
            var prefill = new AddBackorderDialog.EntryLine
            {
                Qty = Math.Clamp(selected.Qty, 1, 10),
                WorkOrder = selected.WorkOrder ?? "",
                SiteReason = selected.SiteReason ?? ""
            };

            var dlg = new AddBackorderDialog(desc, mat, prefill)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dlg.ShowDialog() != true)
                return;

            var line = dlg.ResultLines.FirstOrDefault();
            if (line == null) return;

            // Update selected row (keep DateAdded)
            selected.Qty = Math.Clamp(line.Qty, 1, 10);
            selected.WorkOrder = (line.WorkOrder ?? "").Trim();
            selected.SiteReason = (line.SiteReason ?? "").Trim();

            _backordersView.Refresh();
            SaveBackorders();

            BackorderGrid.ScrollIntoView(selected);
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetCheckedBackordersOrSelected();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one line (use the checkboxes).",
                    "Backorders", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string outDir = GetOutputFolder();

            // Issue template path (261)
            string issueTemplatePath = GetTemplatePath(isReturn: false);
            if (!File.Exists(issueTemplatePath))
            {
                MessageBox.Show("Issue PDF template not found:\n\n" + issueTemplatePath,
                    "Generate", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var (outFile, pages, rows) = GenerateBackordersMergedPdf(selected, outDir, issueTemplatePath);

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outFile,
                        UseShellExecute = true
                    });
                }
                catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Backorder PDF generation failed:\n\n" + ex.Message,
                    "Generate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void EmailDraft_Click(object sender, RoutedEventArgs e)
        {
            await EmailSelectedAsync(openDraft: true);
        }

        private async void Email_Click(object sender, RoutedEventArgs e)
        {
            await EmailSelectedAsync(openDraft: false);
        }

        private async Task EmailSelectedAsync(bool openDraft)
        {
            var selected = GetCheckedBackordersOrSelected();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one line (use the checkboxes).",
                    "Backorders", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ✅ WO checker (required for email actions)
            if (!ConfirmProceedWhenMissingWorkOrders(selected))
                return;

            string outDir = GetOutputFolder();

            string issueTemplatePath = GetTemplatePath(isReturn: false);
            if (!File.Exists(issueTemplatePath))
            {
                MessageBox.Show("Issue PDF template not found:\n\n" + issueTemplatePath,
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string outFile;
            int pages;
            List<PrintTabSummary> rows;

            try
            {
                (outFile, pages, rows) = GenerateBackordersMergedPdf(selected, outDir, issueTemplatePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Backorder PDF generation failed:\n\n" + ex.Message,
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                bool did = await SendPdfEmailOutlookStaAsync(outFile, rows, pages, openDraft);

                if (!did)
                {
                    MessageBox.Show("Email To/CC is blank. Add recipients in Settings → Email.",
                        "Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!openDraft)
                {
                    MessageBox.Show("Email sent successfully.",
                        "Email", MessageBoxButton.OK, MessageBoxImage.Information);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show("Email failed:\n\n" + ex.Message,
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private bool HasMissingWorkOrders(IEnumerable<BackorderLine> lines, out List<BackorderLine> missing)
        {
            missing = lines
                .Where(x => string.IsNullOrWhiteSpace(x.WorkOrder))
                .ToList();

            return missing.Count > 0;
        }

        private bool ConfirmProceedWhenMissingWorkOrders(IEnumerable<BackorderLine> lines)
        {
            if (!HasMissingWorkOrders(lines, out var missing))
                return true;

            MessageBox.Show(
                $"You selected {missing.Count} line(s) with a blank Work Order.\n\n" +
                "Email Draft / Email requires a Work Order.\n\n" +
                "Fix the missing Work Orders (Edit Selected) and try again.",
                "Missing Work Order",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        private string GetOutputFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string outDir =
                !string.IsNullOrWhiteSpace(_settings?.PdfOutputFolder)
                    ? _settings.PdfOutputFolder.Trim()
                    : Path.Combine(baseDir, "Output");

            Directory.CreateDirectory(outDir);
            return outDir;
        }        

        private void SelectAllCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll) return;
            if (SelectAllCheck.IsChecked == null) return; // ignore indeterminate user clicks

            bool check = SelectAllCheck.IsChecked.Value;

            foreach (var line in _backordersView.Cast<BackorderLine>())
                line.IsChecked = check;

            UpdateSelectAllHeader();
        }

        private void HookBackorderLine(BackorderLine line)
        {
            if (line == null) return;

            line.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BackorderLine.IsChecked))
                {
                    UpdateSelectAllHeader();
                    UpdateActionButtonsText();
                }
            };
        }

        private void UpdateSelectAllHeader()
        {
            if (_syncingHeaderCheck) return;
            if (BackorderGrid == null) return;

            var headerCheck = BackorderGrid.Columns
                .Select(c => c.Header)
                .OfType<CheckBox>()
                .FirstOrDefault();

            if (headerCheck == null) return;

            var visible = _backordersView.Cast<object>().OfType<BackorderLine>().ToList();

            _syncingHeaderCheck = true;
            try
            {
                if (visible.Count == 0)
                {
                    headerCheck.IsThreeState = false;
                    headerCheck.IsChecked = false;
                    return;
                }

                bool all = visible.All(x => x.IsChecked);
                bool any = visible.Any(x => x.IsChecked);

                headerCheck.IsThreeState = true;
                headerCheck.IsChecked = all ? true : any ? (bool?)null : false;
            }
            finally
            {
                _syncingHeaderCheck = false;
            }
        }


        private List<BackorderLine> GetCheckedBackordersOrSelected()
        {
            // 1) Prefer checked items (what your buttons now “mean”)
            IEnumerable viewEnumerable = _backordersView as IEnumerable
                                         ?? BackorderGrid?.ItemsSource as IEnumerable
                                         ?? Enumerable.Empty<object>();

            var checkedItems = viewEnumerable
                .Cast<object>()
                .OfType<BackorderLine>()
                .Where(x => x.IsChecked)
                .ToList();

            if (checkedItems.Count > 0)
                return checkedItems;

            // 2) Convenience fallback: use selected rows (supports multi-select)
            if (BackorderGrid?.SelectedItems != null && BackorderGrid.SelectedItems.Count > 0)
            {
                return BackorderGrid.SelectedItems
                    .Cast<object>()
                    .OfType<BackorderLine>()
                    .ToList();
            }

            // 3) Last fallback: single selected row
            if (BackorderGrid?.SelectedItem is BackorderLine one)
                return new List<BackorderLine> { one };

            return new List<BackorderLine>();
        }

        private void UpdateActionButtonsText()
        {
            // ✅ Count checked items from the VIEW (respects your filter)
            int checkedCount = _backordersView
                .Cast<object>()
                .OfType<BackorderLine>()
                .Count(x => x.IsChecked);

            int selectedCount = BackorderGrid?.SelectedItems?.Count ?? 0;

            // ✅ Enable actions if either checked OR selected (matches your fallback logic)
            bool enabled = checkedCount > 0 || selectedCount > 0;

            // Label shows checked count if any, otherwise selected count
            int labelCount = checkedCount > 0 ? checkedCount : selectedCount;

            if (RemoveSelectedButton != null)
            {
                RemoveSelectedButton.Content = labelCount > 0 ? $"Remove Selected ({labelCount})" : "Remove Selected";
                RemoveSelectedButton.IsEnabled = enabled;
            }

            if (GenerateButton != null)
            {
                GenerateButton.Content = labelCount > 0 ? $"Generate ({labelCount})" : "Generate";
                GenerateButton.IsEnabled = enabled;
            }

            if (EmailDraftButton != null)
            {
                EmailDraftButton.Content = labelCount > 0 ? $"Email Draft ({labelCount})" : "Email Draft";
                EmailDraftButton.IsEnabled = enabled;
            }

            if (EmailButton != null)
            {
                EmailButton.Content = labelCount > 0 ? $"Email ({labelCount})" : "Email";
                EmailButton.IsEnabled = enabled;
            }
        }


        private bool _syncingHeaderCheck;

        private void HeaderSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingHeaderCheck) return;
            if (sender is not CheckBox cb) return;

            bool target = cb.IsChecked == true;

            _syncingHeaderCheck = true;
            try
            {
                foreach (var line in _backordersView.Cast<object>().OfType<BackorderLine>())
                    line.IsChecked = target;
            }
            finally
            {
                _syncingHeaderCheck = false;
            }

            UpdateSelectAllHeader();
            UpdateActionButtonsText();
        }


    }
}
