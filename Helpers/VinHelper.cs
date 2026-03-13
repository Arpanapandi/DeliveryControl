using System;

namespace DeliveryControl.Helpers
{
    public static class VinHelper
    {
        public static string Normalize(string? vin)
        {
            if (string.IsNullOrEmpty(vin)) return string.Empty;

            string normalized = vin.Trim().ToUpper();

            // 1. Hapus suffix "X" (Visual only from form)
            if (normalized.EndsWith("X"))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            // 2. Hapus suffix "LB" (From import or internal)
            if (normalized.EndsWith("LB"))
            {
                normalized = normalized.Substring(0, normalized.Length - 2);
            }

            // 3. Hapus prefix "LB"
            if (normalized.StartsWith("LB"))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        public static bool IsMatch(string? vinA, string? vinB)
        {
            return Normalize(vinA) == Normalize(vinB);
        }

        /// <summary>
        /// Validasi apakah label mengandung kode VIN.
        /// Logika: label HARUS mengandung setidaknya prefix dari kode VIN (setelah normalisasi).
        /// Contoh: VIN "NA1580LBX" → normalized "NA1580" → label harus mengandung "NA1580"
        /// Atau VIN "NA1580LB" → normalized "NA1580" → label harus mengandung "NA1580"
        /// </summary>
        public static bool IsLabelContainsVin(string? label, string? vin)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(vin)) return false;

            string labelUpper = label.Trim().ToUpper();
            string vinUpper   = vin.Trim().ToUpper();

            // Check 1: Label mengandung VIN asli (raw) langsung
            if (labelUpper.Contains(vinUpper)) return true;

            // Check 2: Label mengandung VIN yang sudah di-normalize (tanpa suffix LBX / LB / X)
            string normalizedVin = Normalize(vin);
            if (!string.IsNullOrEmpty(normalizedVin) && labelUpper.Contains(normalizedVin)) return true;

            // Check 3: Label mengandung VIN tanpa suffix "X" saja
            string vinNoX = vinUpper.EndsWith("X") ? vinUpper.Substring(0, vinUpper.Length - 1) : vinUpper;
            if (!string.IsNullOrEmpty(vinNoX) && labelUpper.Contains(vinNoX)) return true;

            // Check 4: Label mengandung VIN tanpa suffix "LB"
            string vinNoLb = vinUpper.EndsWith("LB") ? vinUpper.Substring(0, vinUpper.Length - 2) : vinUpper;
            if (!string.IsNullOrEmpty(vinNoLb) && labelUpper.Contains(vinNoLb)) return true;

            return false;
        }
    }
}
