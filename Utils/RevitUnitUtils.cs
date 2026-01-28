using Autodesk.Revit.DB;

namespace AILayoutAgent.Utils
{
    internal static class RevitUnitUtils
    {
        internal static double FeetToMillimeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_MILLIMETERS);
        }

        internal static double FeetToMeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_METERS);
        }
    }
}

