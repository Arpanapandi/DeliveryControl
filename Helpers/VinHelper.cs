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
    }
}
