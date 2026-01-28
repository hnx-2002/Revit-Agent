using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace AILayoutAgent.Utils
{
    internal static class ViewPlanUtils
    {
        internal static bool TryGetUniqueArabicNumberFromViewName(ViewPlan plan, out string numberToken, out string errorMessage)
        {
            numberToken = null;
            errorMessage = null;

            if (plan == null)
            {
                errorMessage = "Missing plan view.";
                return false;
            }

            var name = plan.Name ?? string.Empty;
            var matches = Regex.Matches(name, @"\d+(?:[\.，]\d+)?");

            var tokens = new List<string>();
            foreach (Match match in matches)
            {
                if (match == null || !match.Success)
                {
                    continue;
                }

                var value = match.Value?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!tokens.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    tokens.Add(value);
                }
            }

            if (tokens.Count == 0)
            {
                errorMessage = "当前视图名称不包含阿拉伯数字，无法确定总体高度。\n示例：楼层平面：6.000结构平面";
                return false;
            }

            var decimalTokens = tokens
                .Where(t => !string.IsNullOrWhiteSpace(t) && (t.Contains(".") || t.Contains("，")))
                .ToList();

            if (decimalTokens.Count > 0)
            {
                numberToken = decimalTokens[0];
                return true;
            }

            numberToken = tokens[0];
            return true;
        }

        internal static bool TryGetLayoutZFromViewNameMeters(
            ViewPlan plan,
            out double z,
            out double heightMeters,
            out string heightLabel,
            out string errorMessage)
        {
            z = 0.0;
            heightMeters = 0.0;
            heightLabel = null;
            errorMessage = null;

            if (!TryGetUniqueArabicNumberFromViewName(plan, out var token, out errorMessage))
            {
                return false;
            }

            var normalizedToken = (token ?? string.Empty).Trim().Replace('，', '.');
            if (!double.TryParse(normalizedToken, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
            {
                errorMessage = "无法解析视图名称中的标高数字：" + token;
                return false;
            }

            bool looksLikeMeters = normalizedToken.Contains(".");
            if (!looksLikeMeters && numeric <= 100.0)
            {
                looksLikeMeters = true;
            }

            double heightMm = looksLikeMeters ? numeric * 1000.0 : numeric;
            heightMeters = heightMm / 1000.0;
            heightLabel = looksLikeMeters
                ? normalizedToken
                : heightMeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            const double mmPerFoot = 304.8;
            z = heightMm / mmPerFoot;
            return true;
        }
    }
}
