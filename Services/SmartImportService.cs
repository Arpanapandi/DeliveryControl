using ClosedXML.Excel;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Smart Import Service — auto-detect customer & extract (Manifest, PartNo, Qty)
    /// from various file formats (PDF, Excel, CSV).
    /// Supports AHM, ADM, TMMIN, Sanoh, AWI and generic formats.
    /// </summary>
    public class SmartImportService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SmartImportService> _logger;

        public SmartImportService(ApplicationDbContext db, ILogger<SmartImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        #region === Result Models ===

        public class SmartImportResult
        {
            public bool Success { get; set; }
            public string? DetectedCustomerName { get; set; }
            public string? DetectedCustomerCode { get; set; }
            public int? CustomerId { get; set; }
            public string? DetectedDock { get; set; }
            public string? DetectedManifest { get; set; }
            /// <summary>Route yang terdeteksi dari file (e.g. "IKP", "MR5SU-K1")</summary>
            public string? DetectedRoute { get; set; }
            /// <summary>Cycle yang terdeteksi dari file (e.g. "C1", "203")</summary>
            public string? DetectedCycle { get; set; }
            public string? FileFormat { get; set; }
            public string? ParserUsed { get; set; }
            public List<ExtractedItem> Items { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string? ErrorMessage { get; set; }
            /// <summary>
            /// All matching docks (Customer rows) for the detected customer name.
            /// Admin picks which dock to assign for the delivery schedule.
            /// </summary>
            public List<MatchedDock> MatchedDocks { get; set; } = new();
        }

        public class ExtractedItem
        {
            public string Manifesting { get; set; } = "";
            public string PartNo { get; set; } = "";
            public string PartName { get; set; } = "";
            public int Qty { get; set; }
        }

        public class MatchedDock
        {
            public int CustomerId { get; set; }
            public string CustomerCode { get; set; } = "";
            public string DockName { get; set; } = "";   // CustomerName = dock
            public string? Route { get; set; }
            public string? Cycle { get; set; }
            public string? Docking { get; set; }
            public string? Area { get; set; }
        }

        #endregion

        #region === Customer Detection Profiles ===

        private class DetectionProfile
        {
            public string Name { get; set; } = "";
            public string[] Keywords { get; set; } = Array.Empty<string>();
            public int MinKeywordMatch { get; set; } = 2;
        }

        private static readonly DetectionProfile[] Profiles = new[]
        {
            new DetectionProfile
            {
                Name = "AHM",
                Keywords = new[] { "ASTRA HONDA MOTOR", "AHM/VIN", "AHM/CKD", "GATE :", "Plant I" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "ADM",
                // PDF Delivery Note ADM: header berisi kata-kata khas ini
                Keywords = new[] { "ASTRA DAIHATSU MOTOR", "CYCLE ISSUE", "GROUP ROUTE", "E/G KARAWANG", "ASSEMBLY PLANT",
                                   "DELIVERY NOTE", "MATERIAL No", "TOTAL QTY(PCS)", "TOTAL KBN", "DN NO." },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "TMMIN",
                Keywords = new[] { "SUPPLIER MANIFEST", "TMMIN", "DOCK CODE", "P-LANE CODE", "QTY OF SKID", "MANIFEST NO", "CS ROUTE", "1st TMMIN" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "SANOH",
                Keywords = new[] { "SANOH INDONESIA", "Planned Receipt Date", "DN Number", "PT. SANOH" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "AWI",
                Keywords = new[] { "Parts Center Awi", "Receiving AWI", "Ware House", "ASTRA WHEEL" },
                MinKeywordMatch = 1
            },
            new DetectionProfile
            {
                Name = "KAYABA",
                Keywords = new[] { "KAYABA INDONESIA", "KYB", "DELIVERY ORDER" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "DENSO",
                Keywords = new[] { "DENSO INDONESIA", "DENSO", "DELIVERY NOTE" },
                MinKeywordMatch = 2
            }
        };

        #endregion

        #region === Main Entry Point ===

        public async Task<SmartImportResult> ProcessFileAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();
            _logger.LogInformation("SmartImport: Processing '{FileName}' ({Length} bytes, {Ext})",
                file.FileName, file.Length, ext);

            SmartImportResult result;

            try
            {
                result = ext switch
                {
                    ".pdf" => await ProcessPdfAsync(file),
                    ".xlsx" or ".xls" => ProcessExcel(file),
                    ".csv" or ".txt" => await ProcessTextAsync(file),
                    _ => new SmartImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Format file '{ext}' tidak didukung. Gunakan PDF, Excel (.xlsx/.xls), CSV, atau TXT."
                    }
                };

                if (result.Success && result.Items.Any())
                {
                    // Remove duplicates — key = Manifest + PartNo (same part from same manifest = duplicate)
                    result.Items = result.Items
                        .GroupBy(i => (i.Manifesting.ToUpper(), i.PartNo.ToUpper()))
                        .Select(g => new ExtractedItem
                        {
                            Manifesting = g.First().Manifesting,
                            PartNo = g.First().PartNo,
                            PartName = g.First().PartName,
                            Qty = g.Sum(x => x.Qty) // sum qty if truly same part+manifest
                        })
                        .ToList();

                    // Match detected customer to database
                    await MatchCustomerToDatabase(result);
                }

                result.FileFormat = ext.TrimStart('.');
                _logger.LogInformation("SmartImport: Done — Customer={Customer}, Items={Count}, Success={Success}",
                    result.DetectedCustomerName, result.Items.Count, result.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmartImport: Error processing '{FileName}'", file.FileName);
                result = new SmartImportResult
                {
                    Success = false,
                    ErrorMessage = $"Error memproses file: {ex.Message}"
                };
            }

            return result;
        }

        #endregion

        #region === PDF Processing ===

        private async Task<SmartImportResult> ProcessPdfAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            string allText;
            var allLines = new List<string>();
            // Per-page data untuk extractor yang butuh konteks per-halaman (TMMIN, ADM multi-page)
            var pagesData = new List<(string PageText, List<string> PageLines)>();

            using (var pdf = PdfDocument.Open(stream))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    var pageText = page.Text ?? "";
                    sb.AppendLine(pageText);

                    // Extract word-by-word grouped into lines by Y position (per halaman)
                    var words = page.GetWords().ToList();
                    var pageLines = GroupWordsIntoLines(words);

                    // Jika GroupWordsIntoLines menghasilkan sedikit baris padahal ada teks,
                    // fallback: split pageText berdasarkan newline sebagai tambahan
                    if (pageLines.Count < 3 && pageText.Contains('\n'))
                    {
                        var textLines = pageText.Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();
                        // Gabungkan keduanya — pageLines prioritas (lebih akurat posisi)
                        foreach (var tl in textLines)
                        {
                            if (!pageLines.Any(pl => pl.Contains(tl.Substring(0, Math.Min(tl.Length, 10)))))
                                pageLines.Add(tl);
                        }
                    }

                    allLines.AddRange(pageLines);
                    pagesData.Add((pageText, pageLines));
                }
                allText = sb.ToString();
            }

            _logger.LogInformation("SmartImport PDF: {Pages} halaman, {Lines} lines total, {Chars} chars",
                pagesData.Count, allLines.Count, allText.Length);

            // Detect customer
            string? detectedCustomer = DetectCustomer(allText);

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN"
            };

            // Extract manifest number
            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);

            // Extract dock info + route/cycle (untuk filter dock yang lebih akurat)
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer);
            var (detectedRoute, detectedCycle) = ExtractRouteCycle(allText, detectedCustomer);
            result.DetectedRoute = detectedRoute;
            result.DetectedCycle = detectedCycle;

            // Extract items — TMMIN/ADM pakai extractor per-halaman agar manifest per-page terbaca semua
            if (detectedCustomer == "TMMIN" || detectedCustomer == "ADM")
            {
                result.Items = ExtractItemsMultiPage(pagesData, detectedCustomer, result.DetectedManifest);
            }
            else
            {
                result.Items = ExtractItemsFromText(allText, allLines, detectedCustomer, result.DetectedManifest);
            }

            // If items have manifest from per-line extraction (better than header detection), use first item's manifest
            if (result.Items.Any() && !string.IsNullOrWhiteSpace(result.Items.First().Manifesting))
            {
                var itemManifest = result.Items.First().Manifesting;
                // Only override if DetectedManifest is empty/null or non-numeric (e.g. "SUPPLIER")
                if (string.IsNullOrWhiteSpace(result.DetectedManifest) ||
                    !Regex.IsMatch(result.DetectedManifest, @"^\d+$"))
                {
                    result.DetectedManifest = itemManifest;
                }
            }

            if (!result.Items.Any())
            {
                result.Warnings.Add("Tidak ada item yang berhasil diekstrak dari PDF. Sistem mencoba berbagai pattern Part Number.");
            }

            if (detectedCustomer == null)
            {
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");
            }

            result.ParserUsed = detectedCustomer ?? "GENERIC";
            return result;
        }

        /// <summary>
        /// Ekstrak item dari PDF multi-halaman dengan memproses setiap halaman secara terpisah.
        /// Ini memastikan MANIFEST NO. di header setiap halaman terbaca dan di-assign ke item di halaman itu.
        /// </summary>
        private List<ExtractedItem> ExtractItemsMultiPage(
            List<(string PageText, List<string> PageLines)> pages,
            string? customer,
            string? headerManifest)
        {
            var allItems = new List<ExtractedItem>();

            // Manifest aktif dari halaman sebelumnya — di-carry-over jika halaman baru tidak punya manifest header
            string carryManifest = headerManifest ?? "";

            for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
            {
                var (pageText, pageLines) = pages[pageIdx];

                // Jika halaman ini tidak punya baris item sama sekali (misalnya halaman cover/ringkasan), skip
                // tapi tetap cek manifest di halaman ini untuk carry-over
                List<ExtractedItem> pageItems;
                if (customer == "TMMIN")
                {
                    pageItems = ExtractItemsTmmin(pageText, pageLines, carryManifest);
                }
                else if (customer == "ADM")
                {
                    pageItems = ExtractItemsAdm(pageText, pageLines, carryManifest);
                }
                else
                {
                    pageItems = ExtractItemsFromText(pageText, pageLines, customer, carryManifest);
                }

                // Update carry manifest: jika halaman ini menghasilkan item, ambil manifest dari item terakhir
                // sehingga halaman berikutnya yang tidak punya manifest header tetap pakai manifest yang benar
                if (pageItems.Any())
                {
                    var lastManifest = pageItems.Last().Manifesting;
                    if (!string.IsNullOrWhiteSpace(lastManifest))
                        carryManifest = lastManifest;
                }
                else
                {
                    // Tidak ada item di halaman ini — cek apakah ada manifest number di teks halaman ini
                    // untuk di-carry ke halaman berikutnya
                    var manifestOnPage = ExtractManifestNumber(pageText, customer);
                    if (!string.IsNullOrWhiteSpace(manifestOnPage))
                        carryManifest = manifestOnPage;
                }

                // Merge items — hindari duplikat (manifest+partno yang sama)
                foreach (var item in pageItems)
                {
                    bool isDuplicate = allItems.Any(existing =>
                        existing.PartNo.Equals(item.PartNo, StringComparison.OrdinalIgnoreCase) &&
                        existing.Manifesting.Equals(item.Manifesting, StringComparison.OrdinalIgnoreCase));

                    if (!isDuplicate)
                        allItems.Add(item);
                }

                _logger.LogInformation("SmartImport {Customer} Page {Page}/{Total}: {Count} items, manifest='{Manifest}'",
                    customer, pageIdx + 1, pages.Count, pageItems.Count, carryManifest);
            }

            _logger.LogInformation("SmartImport {Customer} Total: {Count} items dari {Pages} halaman",
                customer, allItems.Count, pages.Count);

            return allItems;
        }

        private List<string> GroupWordsIntoLines(List<Word> words)
        {
            if (!words.Any()) return new List<string>();

            var sorted = words.OrderBy(w => -w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
            var lines = new List<List<Word>>();
            var currentLine = new List<Word> { sorted[0] };
            var currentY = sorted[0].BoundingBox.Bottom;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].BoundingBox.Bottom - currentY) < 3)
                {
                    currentLine.Add(sorted[i]);
                }
                else
                {
                    lines.Add(currentLine.OrderBy(w => w.BoundingBox.Left).ToList());
                    currentLine = new List<Word> { sorted[i] };
                    currentY = sorted[i].BoundingBox.Bottom;
                }
            }
            lines.Add(currentLine.OrderBy(w => w.BoundingBox.Left).ToList());

            return lines.Select(line => string.Join(" ", line.Select(w => w.Text))).ToList();
        }

        #endregion

        #region === Excel Processing ===

        private SmartImportResult ProcessExcel(IFormFile file)
        {
            using var stream = new MemoryStream();
            file.CopyTo(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);

            // Read all text from first 30 rows for customer detection
            var allTextSb = new System.Text.StringBuilder();
            var ws = workbook.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int r = 1; r <= Math.Min(lastRow, 30); r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 20); c++)
                    allTextSb.Append(ws.Cell(r, c).GetString() + " ");
                allTextSb.AppendLine();
            }

            var allText = allTextSb.ToString();
            string? detectedCustomer = DetectCustomer(allText);

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN",
                ParserUsed = (detectedCustomer ?? "GENERIC") + "_EXCEL"
            };

            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer);
            var (detectedRouteExcel, detectedCycleExcel) = ExtractRouteCycle(allText, detectedCustomer);
            result.DetectedRoute = detectedRouteExcel;
            result.DetectedCycle = detectedCycleExcel;

            // Extract from Excel: find header row with Part No / Qty columns
            result.Items = ExtractItemsFromExcel(ws, lastRow, lastCol, detectedCustomer, result.DetectedManifest);

            if (!result.Items.Any())
                result.Warnings.Add("Tidak ada item yang berhasil diekstrak dari Excel.");

            if (detectedCustomer == null)
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");

            return result;
        }

        #endregion

        #region === CSV / TXT Processing ===

        private async Task<SmartImportResult> ProcessTextAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();
            using var reader = new StreamReader(file.OpenReadStream());
            var allText = await reader.ReadToEndAsync();

            // For CSV, split by comma/semicolon; for TXT, split by newline
            var lines = allText.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // For tab/semicolon-delimited, expand each line
            var expandedLines = new List<string>();
            foreach (var line in lines)
            {
                expandedLines.Add(line.Trim());
                // Also add sub-parts split by tab or semicolon for better parsing
                if (line.Contains('\t') || line.Contains(';'))
                {
                    var parts = line.Split(new[] { '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                        if (!string.IsNullOrWhiteSpace(p)) expandedLines.Add(p.Trim());
                }
            }

            string? detectedCustomer = DetectCustomer(allText);

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN",
                ParserUsed = ext == ".csv" ? "CSV" : "TXT"
            };

            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer);
            var (detectedRouteTxt, detectedCycleTxt) = ExtractRouteCycle(allText, detectedCustomer);
            result.DetectedRoute = detectedRouteTxt;
            result.DetectedCycle = detectedCycleTxt;
            result.Items = ExtractItemsFromText(allText, lines, detectedCustomer, result.DetectedManifest);

            if (!result.Items.Any())
                result.Warnings.Add($"Tidak ada item yang berhasil diekstrak dari file {ext.TrimStart('.')}.");

            if (detectedCustomer == null)
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");

            return result;
        }

        #endregion

        #region === Customer Detection ===

        private string? DetectCustomer(string text)
        {
            foreach (var profile in Profiles)
            {
                int matchCount = profile.Keywords
                    .Count(kw => text.Contains(kw, StringComparison.OrdinalIgnoreCase));

                if (matchCount >= profile.MinKeywordMatch)
                {
                    _logger.LogInformation("SmartImport: Detected '{Name}' ({Count}/{Total} keywords)",
                        profile.Name, matchCount, profile.Keywords.Length);
                    return profile.Name;
                }
            }
            return null;
        }

        #endregion

        #region === Manifest Number Extraction ===

        private string? ExtractManifestNumber(string text, string? customer)
        {
            // Customer-specific patterns first
            var patterns = new List<string>();

            switch (customer)
            {
                case "AHM":
                    patterns.Add(@"No\s*DN\s*:\s*(\S+)");
                    patterns.Add(@"AHM[/\\](?:VIN|CKD)[/\\](\d+)");
                    break;
                case "ADM":
                    // ADM DN NO format: "DN27A02602130173" — DN + 2digit + huruf + digits, total ~16 char
                    // Pola di PDF: "DN NO. : DN27A02602130173"
                    patterns.Add(@"DN\s*NO\.?\s*:?\s*(DN[A-Z0-9]{10,20})");
                    // Fallback: apapun setelah "DN NO." yang panjangnya >= 10
                    patterns.Add(@"DN\s*NO\.?\s*[:\s]+([A-Z0-9]{10,25})");
                    // Fallback lama (sebelumnya)
                    patterns.Add(@"DN\d{2}[A-Z]\d{10,}");
                    break;
                case "TMMIN":
                    // TMMIN: "MANIFEST NO." diikuti angka 7-13 digit (e.g. 6260007021)
                    patterns.Add(@"MANIFEST\s*NO\.?\s*[:\s]*(\d{7,13})");
                    // Fallback: angka 10 digit standalone
                    patterns.Add(@"(?<!\d)(\d{10})(?!\d)");
                    // TMMIN only uses numeric manifest — don't fall through to generic patterns
                    foreach (var p in patterns)
                    {
                        var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Length >= 7)
                            return m.Groups[1].Value.Trim();
                    }
                    return null; // Don't try generic patterns for TMMIN
                case "SANOH":
                    patterns.Add(@"DN\s*Number\s*:?\s*([A-Z0-9]+)");
                    break;
                case "AWI":
                    patterns.Add(@"DN\s*NO\.?\s*:?\s*([A-Z0-9\-/]+)");
                    break;
            }

            // Generic patterns (only for non-TMMIN customers)
            patterns.Add(@"DN\s*(?:NO|Number)\.?\s*:?\s*([A-Z0-9\-/]+)");
            patterns.Add(@"(?:No|Nomor)\s*(?:DN|SJ|DO)\s*:?\s*([A-Z0-9\-/]+)");
            patterns.Add(@"No\s*DN\s*:\s*(\S+)");

            foreach (var pattern in patterns)
            {
                var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Length >= 3)
                    return m.Groups[1].Value.Trim();
            }

            return null;
        }

        #endregion

        #region === Dock Info Extraction ===

        private string? ExtractDockInfo(string text, string? customer)
        {
            switch (customer)
            {
                case "AHM":
                    var gateMatch = Regex.Match(text, @"GATE\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
                    if (gateMatch.Success) return gateMatch.Groups[1].Value;
                    break;
                case "TMMIN":
                    // DOCK CODE bisa berisi angka dan/atau huruf (misal: "6I", "43", "53")
                    var dockMatch = Regex.Match(text, @"DOCK\s*CODE\s*[:\s]*([A-Z0-9]{1,5})", RegexOptions.IgnoreCase);
                    if (dockMatch.Success) return "DOCK " + dockMatch.Groups[1].Value;
                    break;
                case "ADM":
                    // GROUP ROUTE di PDF ADM: "GROUP ROUTE : IKP"
                    var routeMatch = Regex.Match(text, @"GROUP\s*ROUTE\s*[:\s]+([A-Z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (routeMatch.Success) return routeMatch.Groups[1].Value.Trim().ToUpper();
                    break;
            }
            return null;
        }

        /// <summary>
        /// Ekstrak Route dan Cycle dari file untuk menyaring dock yang cocok.
        /// Dipanggil khusus untuk ADM — GROUP ROUTE dan CYCLE ISSUE ada di header PDF.
        /// </summary>
        private (string? route, string? cycle) ExtractRouteCycle(string text, string? customer)
        {
            string? route = null;
            string? cycle = null;

            switch (customer)
            {
                case "ADM":
                    // "GROUP ROUTE : IKP"
                    var routeM = Regex.Match(text, @"GROUP\s*ROUTE\s*[:\s]+([A-Z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (routeM.Success) route = routeM.Groups[1].Value.Trim().ToUpper();

                    // "CYCLE ISSUE : 203"  atau "TIME/SEQ : 01:00:00 / 173" — cycle biasanya C1/C2 tapi ADM pakai angka
                    // Di Delivery Note ADM tidak ada kolom "CYCLE" eksplisit, ambil dari TIME/SEQ sequence: "/ 173" → "173"
                    // Atau cek dari kolom "CYCLE ISSUE" jika ada
                    var cycleM = Regex.Match(text, @"CYCLE\s*ISSUE\s*[:\s]+(\S+)", RegexOptions.IgnoreCase);
                    if (cycleM.Success) cycle = cycleM.Groups[1].Value.Trim();
                    break;

                case "TMMIN":
                    // CS ROUTE di TMMIN
                    var tmminRoute = Regex.Match(text, @"CS\s*ROUTE\s*[:\s]+([A-Z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (tmminRoute.Success) route = tmminRoute.Groups[1].Value.Trim().ToUpper();
                    break;
            }

            return (route, cycle);
        }

        #endregion

        #region === Item Extraction (Text/PDF) ===

        private List<ExtractedItem> ExtractItemsFromText(string allText, List<string> lines, string? customer, string? manifest)
        {
            // Use customer-specific extractor if available
            if (customer == "TMMIN")
                return ExtractItemsTmmin(allText, lines, manifest);
            if (customer == "ADM")
                return ExtractItemsAdm(allText, lines, manifest);

            var items = new List<ExtractedItem>();

            // Part number patterns — ordered from most specific to generic
            var partPatterns = GetPartNumberPatterns(customer);

            // Try to find manifest number per-block/per-page
            // Build a map: line index → manifest active at that point
            string activeManifest = manifest ?? "";

            foreach (var line in lines)
            {
                // Check if this line contains a manifest/DN number — update active manifest
                var manifestOnLine = TryExtractManifestFromLine(line, customer);
                if (!string.IsNullOrEmpty(manifestOnLine))
                {
                    activeManifest = manifestOnLine;
                    continue; // manifest line itself — no part number here
                }

                foreach (var pattern in partPatterns)
                {
                    var partMatch = Regex.Match(line, pattern);
                    if (!partMatch.Success) continue;

                    var partNo = partMatch.Groups[1].Success ? partMatch.Groups[1].Value : partMatch.Value;
                    partNo = partNo.Replace(" ", "").Trim().ToUpper();

                    // Skip if looks like a date or phone number
                    if (Regex.IsMatch(partNo, @"^\d{2}[-/]\d{2}[-/]\d{4}$")) continue;
                    if (partNo.Length < 5) continue;

                    // Extract quantity — find numbers in the line
                    int qty = ExtractQuantity(line, partNo);
                    if (qty <= 0) continue;

                    // Extract part name
                    string partName = ExtractPartName(line, partMatch);

                    // Avoid exact duplicates (same manifest + part)
                    if (items.Any(i =>
                        i.PartNo.Equals(partNo, StringComparison.OrdinalIgnoreCase) &&
                        i.Manifesting.Equals(activeManifest, StringComparison.OrdinalIgnoreCase))) continue;

                    items.Add(new ExtractedItem
                    {
                        Manifesting = activeManifest,
                        PartNo = partNo,
                        PartName = partName,
                        Qty = qty
                    });

                    break; // One match per line
                }
            }

            return items;
        }

        /// <summary>
        /// TMMIN-specific extractor — reads "MANIFEST NO." header + item rows (NO | PART NO | PART NAME | ... | TTL QTY)
        /// </summary>
        private List<ExtractedItem> ExtractItemsTmmin(string allText, List<string> lines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();

            // TMMIN part number: 5digit-5alphanumeric-2digit  e.g. 44750-VT010-00, 16261-0Y030-00
            var partPattern = new Regex(@"(\d{5}-[A-Z0-9]{5}-\d{2})", RegexOptions.IgnoreCase);

            // Per-manifest tracking: each manifest block may have its own MANIFEST NO.
            // Scan lines, update activeManifest whenever we see "MANIFEST NO." pattern
            // Only use headerManifest if it's purely numeric (not "SUPPLIER" etc.)
            string activeManifest = (!string.IsNullOrWhiteSpace(headerManifest) && Regex.IsMatch(headerManifest, @"^\d+$"))
                ? headerManifest : "";

            var manifestLineRegex = new Regex(@"MANIFEST\s*NO\.?\s*[:\s]*(\d{7,13})", RegexOptions.IgnoreCase);
            var tenDigitRegex = new Regex(@"(?<!\d)(\d{10})(?!\d)");

            foreach (var line in lines)
            {
                // Update manifest if this line has a MANIFEST NO.
                var mMatch = manifestLineRegex.Match(line);
                if (mMatch.Success)
                {
                    activeManifest = mMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Also detect standalone 10-digit number in a short line (e.g. "6260007021")
                if (line.Trim().Length <= 20)
                {
                    var tenMatch = tenDigitRegex.Match(line.Trim());
                    if (tenMatch.Success)
                    {
                        activeManifest = tenMatch.Groups[1].Value;
                        continue;
                    }
                }

                // Look for part number in line
                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpper();

                // Extract TTL QTY — TMMIN table: NO | PART NO | PART NAME | UNIQ NO | BOX TYPE | PCS/KBN | NO of KBN | TTL QTY
                // TTL QTY is the LAST numeric value on the line (after the part number)
                var afterPart = line.Substring(partMatch.Index + partMatch.Length);
                var allNumbers = Regex.Matches(afterPart, @"(?<!\d)(\d{1,3}(?:\.\d{3})*|\d{1,6})(?!\d)")
                    .Cast<Match>()
                    .Select(m => {
                        int.TryParse(m.Groups[1].Value.Replace(".", ""), out int n);
                        return n;
                    })
                    .Where(n => n > 0)
                    .ToList();

                if (!allNumbers.Any()) continue;

                // TTL QTY = last number (rightmost column)
                int qty = allNumbers.Last();
                if (qty <= 0) continue;

                // Part name = text between row-number and part-number
                string partName = ExtractPartName(line, partMatch);

                // Avoid duplicate manifest+part combination
                if (items.Any(i =>
                    i.PartNo.Equals(partNo, StringComparison.OrdinalIgnoreCase) &&
                    i.Manifesting.Equals(activeManifest, StringComparison.OrdinalIgnoreCase))) continue;

                items.Add(new ExtractedItem
                {
                    Manifesting = activeManifest,
                    PartNo = partNo,
                    PartName = partName,
                    Qty = qty
                });
            }

            _logger.LogInformation("SmartImport TMMIN: {Count} items extracted with manifests: [{Manifests}]",
                items.Count,
                string.Join(", ", items.Select(i => i.Manifesting).Distinct()));

            return items;
        }

        /// <summary>
        /// ADM-specific extractor — Delivery Note PT. Astra Daihatsu Motor
        /// Format tabel: No | Material No | Material Name | Total Kanban | Packing Type | Total QTY(PCS)
        /// DN NO format : DN27A02602130173
        /// Part No      : 12261-0Y041-00-87  (5digit-5alphanum-2digit-2digit)
        /// QTY          : kolom TOTAL QTY(PCS) = angka paling kanan di baris item
        /// </summary>
        private List<ExtractedItem> ExtractItemsAdm(string allText, List<string> lines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();

            // ADM Material No: 5digit-5alphanum-2digit[-2digit]
            // Contoh: 12261-0Y041-00-87, 12261-BZ310-00, 17340-BZ010-00-87
            var partPattern = new Regex(
                @"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2}(?:\s*-\s*\d{2})?)",
                RegexOptions.IgnoreCase);

            // DN NO di PDF ADM: "DN NO. : DN27A02602130173"
            var dnNoRegex = new Regex(
                @"DN\s*NO\.?\s*[:\s]+(DN[A-Z0-9]{10,20})",
                RegexOptions.IgnoreCase);
            // Fallback DN NO tanpa prefix "DN" di value
            var dnNoFallbackRegex = new Regex(
                @"DN\s*NO\.?\s*[:\s]+([A-Z0-9]{10,25})",
                RegexOptions.IgnoreCase);

            // Cari DN NO dari full text dulu (bisa di header, sebelum tabel item)
            string activeManifest = headerManifest ?? "";
            if (string.IsNullOrWhiteSpace(activeManifest))
            {
                var dnMatch = dnNoRegex.Match(allText);
                if (!dnMatch.Success) dnMatch = dnNoFallbackRegex.Match(allText);
                if (dnMatch.Success) activeManifest = dnMatch.Groups[1].Value.Trim().ToUpper();
            }

            foreach (var line in lines)
            {
                // Update DN NO jika ada baris yang mengandung "DN NO"
                var dnLineMatch = dnNoRegex.Match(line);
                if (!dnLineMatch.Success) dnLineMatch = dnNoFallbackRegex.Match(line);
                if (dnLineMatch.Success)
                {
                    activeManifest = dnLineMatch.Groups[1].Value.Trim().ToUpper();
                    continue;
                }

                // Cari Material No di baris
                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value
                    .Replace(" ", "").Trim().ToUpper();

                // Skip baris header tabel
                if (partNo.StartsWith("MATERIAL", StringComparison.OrdinalIgnoreCase)) continue;

                // -----------------------------------------------------------------------
                // QTY = TOTAL QTY(PCS) = angka paling kanan setelah Material No
                // Struktur kolom: No | Material No | Material Name | Total Kanban | Packing Type | Total QTY(PCS)
                // Kita ambil angka terakhir (paling kanan) setelah part number
                // -----------------------------------------------------------------------
                var afterPart = line.Substring(partMatch.Index + partMatch.Length);
                var allNumbers = Regex.Matches(afterPart, @"(?<!\d)(\d{1,3}(?:,\d{3})*|\d{1,7})(?!\d)")
                    .Cast<Match>()
                    .Select(m => {
                        int.TryParse(m.Groups[1].Value.Replace(",", ""), out int n);
                        return n;
                    })
                    .Where(n => n > 0)
                    .ToList();

                if (!allNumbers.Any()) continue;

                // Total QTY(PCS) = angka terakhir di baris (kolom paling kanan)
                int qty = allNumbers.Last();
                if (qty <= 0) continue;

                // Part name = teks antara nomor urut dan Material No
                string partName = ExtractPartName(line, partMatch);

                // Hindari duplikat
                if (items.Any(i =>
                    i.PartNo.Equals(partNo, StringComparison.OrdinalIgnoreCase) &&
                    i.Manifesting.Equals(activeManifest, StringComparison.OrdinalIgnoreCase))) continue;

                items.Add(new ExtractedItem
                {
                    Manifesting = activeManifest,
                    PartNo = partNo,
                    PartName = partName,
                    Qty = qty
                });
            }

            _logger.LogInformation("SmartImport ADM: {Count} items extracted, manifest='{Manifest}'",
                items.Count, activeManifest);

            return items;
        }

        /// <summary>
        /// Try to detect a manifest/DN number from a single line (for non-TMMIN customers).
        /// Returns null if this line doesn't look like a manifest line.
        /// </summary>
        private string? TryExtractManifestFromLine(string line, string? customer)
        {
            // Only try if the line has a manifest keyword
            if (!Regex.IsMatch(line, @"MANIFEST|DN\s*NO|DN\s*NUMBER|NOMOR\s*DN|No\.?\s*SJ", RegexOptions.IgnoreCase))
                return null;

            var m = Regex.Match(line, @"(?:MANIFEST\s*NO\.?|DN\s*(?:NO|NUMBER)\.?)\s*[:\s]*([A-Z0-9]{5,20})", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            return null;
        }

        private List<string> GetPartNumberPatterns(string? customer)
        {
            var patterns = new List<string>();

            switch (customer)
            {
                case "AHM":
                    // AHM: 11103-K0J -N000-H1 (5digit-3char-4char-2char, may have spaces)
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{3}\s*-\s*[A-Z0-9]{4}\s*-\s*[A-Z0-9]{2})");
                    break;
                case "ADM":
                    // ADM: 12261-0Y041-00-87 or 12345-BZ010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2}(?:\s*-\s*\d{2})?)");
                    break;
                case "TMMIN":
                    // TMMIN: 44750-VT010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2})");
                    break;
                case "SANOH":
                    // Sanoh: GI272-0J020 or similar
                    patterns.Add(@"([A-Z]{1,2}\d{3,4}\s*-\s*[A-Z0-9]{4,6})");
                    break;
                case "AWI":
                    // AWI: 33519-BZ010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2})");
                    break;
            }

            // Generic patterns (fallback for all)
            patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{3,5}\s*-\s*[A-Z0-9]{2,5}(?:\s*-\s*[A-Z0-9]{2})?)"); // 5digit-XXX-XXXX style
            patterns.Add(@"([A-Z]{1,3}\d{3,5}\s*-\s*[A-Z0-9]{3,6})"); // Letter-prefix parts

            return patterns;
        }

        private int ExtractQuantity(string line, string partNo)
        {
            // Remove the part number from line to avoid confusion
            var lineWithoutPart = line.Replace(partNo, " ");

            // Find all standalone numbers (including dot-thousands like 1.000, 10.500)
            var numbers = Regex.Matches(lineWithoutPart, @"(?<!\S)(\d{1,3}(?:\.\d{3})*|\d{1,7})(?:[\s,;|]|$)")
                .Cast<Match>()
                .Select(m => {
                    // Handle Indonesian thousand separator: 1.000 → 1000, 10.500 → 10500
                    string raw = m.Groups[1].Value.Replace(".", "").Replace(",", "");
                    int.TryParse(raw, out int n);
                    return n;
                })
                .Where(n => n > 0)
                .ToList();

            if (!numbers.Any()) return 0;

            // Strategy: Qty is typically the last significant number or the largest number
            // Filter out small sequence numbers (1, 2, 3...)
            var candidates = numbers.Where(n => n >= 10).ToList();
            if (candidates.Any())
            {
                // Return the last candidate (usually Qty is at the end of the row)
                return candidates.Last();
            }

            // If all numbers are small, return the last one
            return numbers.Last();
        }

        private string ExtractPartName(string line, Match partMatch)
        {
            try
            {
                // Try to get text before the part number (often the description)
                var beforePart = line.Substring(0, partMatch.Index).Trim();
                // Remove leading row number
                beforePart = Regex.Replace(beforePart, @"^\d+[\.\)\s]+", "").Trim();

                if (!string.IsNullOrEmpty(beforePart) && beforePart.Length > 3)
                    return beforePart;

                // Try text after part number
                var afterPart = line.Substring(partMatch.Index + partMatch.Length).Trim();
                var nameMatch = Regex.Match(afterPart, @"^[\s,]*([A-Za-z][A-Za-z\s,\.]{3,50})");
                if (nameMatch.Success)
                    return nameMatch.Groups[1].Value.Trim();
            }
            catch { }
            return "";
        }

        #endregion

        #region === Item Extraction (Excel) ===

        private List<ExtractedItem> ExtractItemsFromExcel(IXLWorksheet ws, int lastRow, int lastCol, string? customer, string? manifest)
        {
            var items = new List<ExtractedItem>();

            // Step 1: Find header row
            int headerRow = 0;
            int colManifest = 0, colPartNo = 0, colPartName = 0, colQty = 0;

            for (int r = 1; r <= Math.Min(lastRow, 25); r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 20); c++)
                {
                    var val = ws.Cell(r, c).GetString().Trim().ToUpper();

                    if (val.Contains("MANIFEST") || val.Contains("DN NO") || val == "DN")
                    {
                        if (colManifest == 0) colManifest = c;
                        if (headerRow == 0) headerRow = r;
                    }
                    else if (
                        (val.Contains("PART") && (val.Contains("NO") || val.Contains("NUMBER"))) ||
                        val.Contains("NOMOR PART") ||
                        val.Contains("MATERIAL NO") ||  // ADM: "MATERIAL No"
                        val == "MATERIAL NO." ||
                        val == "PART NO / VIN" ||
                        val == "NOMOR PART")
                    {
                        colPartNo = c;
                        if (headerRow == 0) headerRow = r;
                    }
                    else if (val.Contains("DESKRIPSI") || val.Contains("DESCRIPTION") ||
                             (val.Contains("PART") && val.Contains("NAME")) ||
                             (val.Contains("MATERIAL") && val.Contains("NAME")))
                    {
                        colPartName = c;
                    }
                    else if (
                        // ADM: "TOTAL QTY(PCS)" harus diprioritaskan di atas QTY biasa
                        (val.Contains("TOTAL") && val.Contains("QTY")) ||
                        val.Contains("TOTAL QTY(PCS)") ||
                        // Generic
                        val.Contains("QTY") || val.Contains("QUANTITY") ||
                        val.Contains("JUMLAH") || val == "PC" || val == "PCS")
                    {
                        // Prioritaskan "TOTAL QTY" di atas QTY biasa
                        if (colQty == 0 || val.Contains("TOTAL"))
                            colQty = c;
                    }
                }

                // If we found Part and Qty columns, stop scanning
                if (headerRow > 0 && colPartNo > 0 && colQty > 0) break;
            }

            // If no structured header found, try pattern matching on each row
            if (headerRow == 0 || colPartNo == 0)
            {
                _logger.LogInformation("SmartImport Excel: No structured header, using pattern matching");
                return ExtractItemsFromExcelByPattern(ws, lastRow, lastCol, customer, manifest);
            }

            _logger.LogInformation("SmartImport Excel: Header at row {Row}, PartNo=col{P}, Qty=col{Q}",
                headerRow, colPartNo, colQty);

            // Step 2: Read data rows
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var partNo = ws.Cell(r, colPartNo).GetString().Trim();
                if (string.IsNullOrEmpty(partNo)) continue;

                // Must look like a part number (contains digits and at least one dash or letter)
                if (!Regex.IsMatch(partNo, @"[A-Z0-9]{3,}[-]?[A-Z0-9]*", RegexOptions.IgnoreCase)) continue;

                // Skip instruction/header rows
                if (partNo.StartsWith("Keterangan", StringComparison.OrdinalIgnoreCase) ||
                    partNo.StartsWith("Hapus", StringComparison.OrdinalIgnoreCase) ||
                    partNo.StartsWith("Customer", StringComparison.OrdinalIgnoreCase))
                    continue;

                var manifestVal = colManifest > 0 ? ws.Cell(r, colManifest).GetString().Trim() : (manifest ?? "");
                var partName = colPartName > 0 ? ws.Cell(r, colPartName).GetString().Trim() : "";

                int qty = 0;
                if (colQty > 0)
                {
                    var qtyStr = ws.Cell(r, colQty).GetString().Replace(",", "").Trim();
                    int.TryParse(Regex.Match(qtyStr, @"\d+").Value, out qty);
                }

                if (qty <= 0) continue;

                items.Add(new ExtractedItem
                {
                    Manifesting = !string.IsNullOrEmpty(manifestVal) ? manifestVal : (manifest ?? ""),
                    PartNo = partNo.ToUpper(),
                    PartName = partName,
                    Qty = qty
                });
            }

            return items;
        }

        private List<ExtractedItem> ExtractItemsFromExcelByPattern(IXLWorksheet ws, int lastRow, int lastCol, string? customer, string? manifest)
        {
            var items = new List<ExtractedItem>();
            var partPatterns = GetPartNumberPatterns(customer);

            for (int r = 1; r <= lastRow; r++)
            {
                var rowText = "";
                for (int c = 1; c <= Math.Min(lastCol, 20); c++)
                    rowText += ws.Cell(r, c).GetString() + " ";

                foreach (var pattern in partPatterns)
                {
                    var match = Regex.Match(rowText, pattern);
                    if (!match.Success) continue;

                    var partNo = (match.Groups[1].Success ? match.Groups[1].Value : match.Value).Replace(" ", "").Trim();
                    if (partNo.Length < 5) continue;

                    int qty = ExtractQuantity(rowText, partNo);
                    if (qty <= 0) continue;

                    if (!items.Any(i => i.PartNo.Equals(partNo, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new ExtractedItem
                        {
                            Manifesting = manifest ?? "",
                            PartNo = partNo,
                            Qty = qty
                        });
                    }
                    break;
                }
            }

            return items;
        }

        #endregion

        #region === Customer Database Matching ===

        private async Task MatchCustomerToDatabase(SmartImportResult result)
        {
            if (string.IsNullOrEmpty(result.DetectedCustomerName) || result.DetectedCustomerName == "UNKNOWN")
                return;

            var customers = await _db.Customers.Where(c => c.IsActive).ToListAsync();
            
            // Find ALL docks that match the detected customer name
            List<Customer> matchedCustomers = new();

            // 1. Direct code match (e.g. "ADM" == CustomerCode "ADM")
            matchedCustomers = customers.Where(c =>
                !string.IsNullOrWhiteSpace(c.CustomerCode) &&
                c.CustomerCode.Trim().Equals(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();

            // 2. Code contains match — CustomerCode mengandung nama customer yang terdeteksi
            //    Misal detectedName="ADM" → hanya match CustomerCode yang mengandung "ADM"
            //    (tidak ikut match CustomerCode lain seperti "HONDA" hanya karena alias)
            if (!matchedCustomers.Any())
            {
                matchedCustomers = customers.Where(c =>
                    !string.IsNullOrWhiteSpace(c.CustomerCode) &&
                    c.CustomerCode.Contains(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 3. Name contains match
            if (!matchedCustomers.Any())
            {
                matchedCustomers = customers.Where(c =>
                    !string.IsNullOrWhiteSpace(c.CustomerName) &&
                    c.CustomerName.Contains(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 4. Broader keyword/alias match — HANYA jika step 1-3 benar-benar tidak ada hasil
            if (!matchedCustomers.Any())
            {
                var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AHM", new[] { "HONDA", "AHM", "ASTRA HONDA" } },
                    { "ADM", new[] { "DAIHATSU", "ADM", "ASTRA DAIHATSU" } },
                    { "TMMIN", new[] { "TOYOTA", "TMMIN" } },
                    { "SANOH", new[] { "SANOH" } },
                    { "AWI", new[] { "AWI", "ASTRA WHEEL" } },
                    { "KAYABA", new[] { "KAYABA", "KYB" } },
                    { "DENSO", new[] { "DENSO" } },
                };

                if (aliases.TryGetValue(result.DetectedCustomerName, out var terms))
                {
                    foreach (var term in terms)
                    {
                        matchedCustomers = customers.Where(c =>
                            (c.CustomerName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.CustomerCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.Docking ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.Area ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (matchedCustomers.Any()) break;
                    }
                }
            }

            if (matchedCustomers.Any())
            {
                // ── Urutkan dock: yang Route-nya cocok dengan DetectedRoute letakkan paling atas ──
                // Ini membantu pre-select dock yang paling relevan secara otomatis
                if (!string.IsNullOrWhiteSpace(result.DetectedRoute))
                {
                    var routeNorm = result.DetectedRoute.Trim().ToUpper();
                    matchedCustomers = matchedCustomers
                        .OrderByDescending(c =>
                        {
                            // Score: cocokkan Route, Docking, CustomerName, Area dengan detectedRoute
                            int score = 0;
                            if (!string.IsNullOrWhiteSpace(c.Route) &&
                                c.Route.Trim().ToUpper().Contains(routeNorm)) score += 10;
                            if (!string.IsNullOrWhiteSpace(c.Docking) &&
                                c.Docking.Trim().ToUpper().Contains(routeNorm)) score += 5;
                            if (!string.IsNullOrWhiteSpace(c.CustomerName) &&
                                c.CustomerName.Trim().ToUpper().Contains(routeNorm)) score += 5;
                            if (!string.IsNullOrWhiteSpace(c.Area) &&
                                c.Area.Trim().ToUpper().Contains(routeNorm)) score += 3;
                            return score;
                        })
                        .ThenBy(c => c.CustomerCode)
                        .ToList();
                }

                // Populate all matching docks
                result.MatchedDocks = matchedCustomers.Select(c => new MatchedDock
                {
                    CustomerId = c.CustomerId,
                    CustomerCode = c.CustomerCode ?? "",
                    DockName = c.CustomerName ?? "",
                    Route = c.Route,
                    Cycle = c.Cycle,
                    Docking = c.Docking,
                    Area = c.Area
                }).ToList();

                // Set first match sebagai default (sudah diurutkan berdasarkan kecocokan route)
                var first = matchedCustomers.First();
                result.CustomerId = first.CustomerId;
                result.DetectedCustomerCode = first.CustomerCode;
                result.DetectedDock = first.CustomerName;

                _logger.LogInformation(
                    "SmartImport: Matched {Count} dock(s) for '{Name}' (route='{Route}') — best: '{Code}' (ID={Id})",
                    matchedCustomers.Count, result.DetectedCustomerName,
                    result.DetectedRoute ?? "-", first.CustomerCode, first.CustomerId);

                if (matchedCustomers.Count > 1)
                {
                    result.Warnings.Add($"Ditemukan {matchedCustomers.Count} dock untuk customer '{result.DetectedCustomerName}'. Pilih dock yang sesuai.");
                }
            }
            else
            {
                result.Warnings.Add($"Customer '{result.DetectedCustomerName}' tidak ditemukan di database. Pilih customer/dock secara manual.");
            }
        }

        #endregion
    }
}
