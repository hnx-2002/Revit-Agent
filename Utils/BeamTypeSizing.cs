using System;

namespace RevitAgent.Utils
{
    internal static class BeamTypeSizing
    {
        internal static string ComputeBeamTypeName(BeamRole role, double lengthFeet)
        {
            double lengthMm = FeetToMm(lengthFeet);
            double lengthM = lengthMm / 1000.0;

            if (role == BeamRole.Main)
            {
                int widthMm = lengthM < 6.5 ? 250 : 300;
                int heightMm = RoundUpToMultiple((int)Math.Ceiling(lengthMm / 12.0), 50);
                return $"{widthMm}*{heightMm}";
            }

            int secondaryHeightMm = RoundUpToMultiple((int)Math.Ceiling(lengthMm / 15.0), 50);
            if (secondaryHeightMm < 300)
            {
                secondaryHeightMm = 300;
            }

            int secondaryWidthMm;
            if (secondaryHeightMm <= 700)
            {
                secondaryWidthMm = 250;
            }
            else
            {
                secondaryWidthMm = RoundUpToMultiple((int)Math.Ceiling(secondaryHeightMm / 3.0), 50);
            }

            return $"{secondaryWidthMm}*{secondaryHeightMm}";
        }

        internal static bool TryParseSectionMm(string typeName, out int widthMm, out int heightMm)
        {
            widthMm = 0;
            heightMm = 0;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            var s = typeName.Trim()
                .Replace(" ", string.Empty)
                .Replace("×", "*")
                .Replace("x", "*")
                .Replace("X", "*");

            int star = s.IndexOf('*');
            if (star <= 0 || star >= s.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(s.Substring(0, star), out widthMm))
            {
                return false;
            }

            if (!int.TryParse(s.Substring(star + 1), out heightMm))
            {
                return false;
            }

            return widthMm > 0 && heightMm > 0;
        }

        internal static double ComputeBeamWidthFeet(BeamRole role, double lengthFeet)
        {
            var name = ComputeBeamTypeName(role, lengthFeet);
            if (!TryParseSectionMm(name, out int widthMm, out _))
            {
                return 0.0;
            }

            return MmToFeet(widthMm);
        }

        internal static double MmToFeet(double mm)
        {
            const double mmPerFoot = 304.8;
            return mm / mmPerFoot;
        }

        internal static double FeetToMm(double feet)
        {
            const double mmPerFoot = 304.8;
            return feet * mmPerFoot;
        }

        private static int RoundUpToMultiple(int value, int multiple)
        {
            if (multiple <= 0)
            {
                return value;
            }

            int rem = value % multiple;
            if (rem == 0)
            {
                return value;
            }

            return value + (multiple - rem);
        }
    }
}

