using Autodesk.Revit.DB;

namespace RevitAgent.Utils
{
    internal static class ViewPlanUtils
    {
        internal static bool TryGetPlanViewZ(Document doc, ViewPlan plan, out double z)
        {
            z = 0.0;
            if (doc == null || plan == null)
            {
                return false;
            }

            try
            {
                var level = plan.GenLevel ?? doc.GetElement(plan.LevelId) as Level;
                if (level == null)
                {
                    return false;
                }

                z = level.ProjectElevation;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

