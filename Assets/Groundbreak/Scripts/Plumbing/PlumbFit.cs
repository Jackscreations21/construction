using UnityEngine;

namespace Groundbreak
{
    /// <summary>Plumbing fitting helpers on top of the shared RunBuilder: reducing tees / wyes / sanitary tees
    /// (3x3x2 wye, 2x2x1-1/2 santee...) and stand-alone fittings with a reduced branch.</summary>
    public static class PlumbFit
    {
        /// <summary>Tees into a run like RunBuilder.TeeInto, then reduces the branch to branchSpec's size (port size + hub visual).</summary>
        public static NetPort ReducingTee(NetRun run, Vector3 point, Vector3 branchDir, string type, RunSpec branchSpec)
        {
            if (run == null) return null;
            var main = run.Spec;
            var port = RunBuilder.TeeInto(run, point, branchDir, type);
            if (port == null || branchSpec == null || Mathf.Abs(branchSpec.NominalIn - main.NominalIn) < 0.01f) return port;
            Reduce(port.Node, main, branchSpec, 2);
            return port;
        }

        /// <summary>Creates a fitting (dirs: run, run, branch...) whose port #branchIndex is reduced to branchSpec.</summary>
        public static NetNode Create(string type, RunSpec main, Vector3 center, Vector3[] dirs, RunSpec branchSpec, int branchIndex = 2)
        {
            var node = Fittings.Create(type, main, center, dirs);
            if (branchSpec != null && Mathf.Abs(branchSpec.NominalIn - main.NominalIn) > 0.01f) Reduce(node, main, branchSpec, branchIndex);
            return node;
        }

        static void Reduce(NetNode node, RunSpec main, RunSpec br, int bi)
        {
            var bp = node.Ports[bi];
            bp.NominalIn = br.NominalIn;
            node.DisplayName = $"{Fittings.Fraction(main.NominalIn)}\" x {Fittings.Fraction(main.NominalIn)}\" x {Fittings.Fraction(br.NominalIn)}\" " +
                               (node.Type == "santee" ? "sanitary tee" : node.Type);
            var old = node.transform.Find("Visual");
            if (old != null) Object.Destroy(old.gameObject);
            Vector3 c = node.transform.position;
            float leg = Fittings.Leg(main);
            var mm = new MepMesh { Origin = c };
            bool crimp = main.Join == JoinMethod.Crimp;
            var body = MepMesh.For(main, crimp ? 0.8f : 1.06f);
            var bBody = MepMesh.For(br, crimp ? 0.8f : 1.06f);
            for (int i = 0; i < node.Ports.Count; i++)
            {
                if (i == bi) continue;
                var d = node.Ports[i].WorldDir;
                mm.Band(c, c + d * leg * 0.98f, body, 0, false);
                float hl = crimp ? leg * 0.25f : leg * 0.55f;
                mm.Band(c + d * (leg - hl), c + d * leg, MepMesh.For(main, crimp ? 1.02f : 1.13f), 0, true);
            }
            Vector3 bd = bp.WorldDir;
            if (node.Type == "santee")
            {
                Vector3 down = Vector3.down;
                foreach (var p in node.Ports) if (p != bp && Vector3.Dot(p.WorldDir, Vector3.down) > 0.5f) down = p.WorldDir;
                mm.Sweep(MepMesh.ElbowPath(c, bd, down, leg * 0.98f, 8), bBody, 0, false, false);
            }
            else mm.Band(c, c + bd * leg * 0.98f, bBody, 0, false);
            float bhl = crimp ? leg * 0.25f : Mathf.Min(leg * 0.55f, Fittings.Leg(br) * 0.6f);
            mm.Band(c + bd * (leg - bhl), c + bd * leg, MepMesh.For(br, crimp ? 1.02f : 1.13f), 0, true);
            mm.Spawn(node.transform, "Visual", new[] { Fittings.MaterialFor(main) });
        }
    }
}
