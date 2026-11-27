using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Where the "building" of a map model stands inside its spec footprint (model frame: x across the front, z = 0 at the front, -Depth at
    /// the back). For most types that is the whole spec footprint; a gas station's spec includes the pump forecourt (the store is at the back),
    /// a lumber yard's and a contractor yard's spec is the yard (the store / shop is at the front-left). Models size those buildings from
    /// <see cref="MapBuildingSpec.PlanSize"/> when a plan is on file, so the plan's footprint and the drawn building agree.
    /// </summary>
    public static class MapBodies
    {
        /// <summary>Gas station store (width, depth) at the back of the forecourt.</summary>
        public static Vector2 GasStore(MapBuildingSpec s)
        {
            float W = Mathf.Max(20f, s.Width), D = Mathf.Max(24f, s.Depth);
            if (s.PlanSize.x > 0f) return new Vector2(Mathf.Min(s.PlanSize.x, W - 2f), Mathf.Min(s.PlanSize.y, D * 0.45f));
            return new Vector2(Mathf.Min(16f, W - 2f), Mathf.Min(11f, D * 0.33f));
        }

        /// <summary>Lumber yard store / contractor desk (width, depth) at the front-left.</summary>
        public static Vector2 LumberStore(MapBuildingSpec s)
        {
            if (s.PlanSize.x > 0f) return new Vector2(Mathf.Min(s.PlanSize.x, s.Width * 0.38f), Mathf.Min(s.PlanSize.y, s.Depth * 0.45f));
            return new Vector2(Mathf.Min(24f, s.Width * 0.3f), 18f);
        }

        /// <summary>Contractor yard shop (width, depth) at the front-left.</summary>
        public static Vector2 YardShop(MapBuildingSpec s)
        {
            if (s.PlanSize.x > 0f) return new Vector2(Mathf.Min(s.PlanSize.x, s.Width * 0.6f), Mathf.Min(s.PlanSize.y, s.Depth * 0.45f));
            return new Vector2(Mathf.Min(18f, s.Width * 0.6f), Mathf.Min(14f, s.Depth * 0.35f));
        }

        /// <summary>The building's footprint in the model frame: x = left..right, y = back (-z) .. front (z).</summary>
        public static Rect Body(MapBuildingSpec s)
        {
            float hw = s.Width * 0.5f;
            switch (s.Type)
            {
                case "gasstation":
                {
                    var g = GasStore(s); float D = Mathf.Max(24f, s.Depth);
                    return Rect.MinMaxRect(-g.x * 0.5f, -D, g.x * 0.5f, -D + g.y);
                }
                case "lumberyard":
                {
                    var g = LumberStore(s); float x0 = -hw + 1f;
                    return Rect.MinMaxRect(x0, -g.y, x0 + g.x, 0f);
                }
                case "contractoryard":
                {
                    var g = YardShop(s); float x0 = -hw + 0.5f;
                    return Rect.MinMaxRect(x0, -g.y, x0 + g.x, 0f);
                }
                // models with a walk / portico / apron in front of the building inside the spec (same numbers as the models)
                case "stripmall":
                {
                    float W = Mathf.Max(18f, s.Width), D = Mathf.Max(12f, s.Depth);
                    return Rect.MinMaxRect(-W / 2f, -D, W / 2f, -3.2f);
                }
                case "bank":
                {
                    float W = Mathf.Max(10f, s.Width), D = Mathf.Max(10f, s.Depth);
                    return Rect.MinMaxRect(-W / 2f + 0.05f, -D, W / 2f - 0.05f, -4.2f);
                }
                case "autorepair":
                {
                    float W = Mathf.Max(16f, s.Width), D = Mathf.Max(12f, s.Depth);
                    return Rect.MinMaxRect(-W / 2f, -D, W / 2f, -Mathf.Clamp(D * 0.3f, 5f, 9f));
                }
                case "postoffice":
                {
                    float W = Mathf.Max(12f, s.Width), D = Mathf.Max(12f, s.Depth);
                    return Rect.MinMaxRect(-W / 2f + 0.1f, -D, W / 2f - 0.1f, -Mathf.Clamp(D * 0.25f, 3.5f, 6f));
                }
                case "fastfood":
                {   // restaurant on the left, drive-thru lane on the right
                    float W = Mathf.Max(16f, s.Width), D = Mathf.Max(16f, s.Depth);
                    float bx0 = -W / 2f + 0.6f, bx1 = Mathf.Max(bx0 + 10f, W / 2f - 4.6f);
                    float bd = s.PlanSize.y > 0f ? s.PlanSize.y : Mathf.Clamp(D * 0.5f, 9f, 13f);
                    return Rect.MinMaxRect(bx0, -1.6f - bd, bx1, -1.6f);
                }
                case "farmhouse":
                {   // the main block (a 7' wrap porch takes one side of the spec width; which side varies)
                    float W = Mathf.Max(8f, s.Width), D = Mathf.Max(8f, s.Depth);
                    return Rect.MinMaxRect(-W / 2f + 1.05f, -D, W / 2f - 1.05f, 0f);
                }
                case "diner":
                {
                    float W = Mathf.Max(10f, s.Width), D = Mathf.Max(7f, s.Depth);
                    bool big = D >= 12f;
                    float L = Mathf.Clamp(W - (big ? 0.5f : 1.5f), 9f, 17f);
                    return big ? Rect.MinMaxRect(-L / 2f, -D + 0.2f, L / 2f, -1.3f) : Rect.MinMaxRect(-L / 2f, -2.4f - Mathf.Clamp(D - 3f, 4.2f, 5.2f), L / 2f, -2.4f);
                }
                default:
                    return Rect.MinMaxRect(-hw, -s.Depth, hw, 0f);
            }
        }

        /// <summary>True for types whose spec is a yard / forecourt with a building inside it (sized by PlanSize).</summary>
        public static bool HasSubBody(string type) => type == "gasstation" || type == "lumberyard" || type == "contractoryard";
    }
}
