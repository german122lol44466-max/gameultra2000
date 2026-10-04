"""Рендер техники v2 в docs/renders/v2/vehicles: /opt/bpyenv/bin/python Tools/Blender/render_vehicles_v2.py [Name ...]"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
from swlib import stage, vehicles as V  # noqa: E402

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "docs", "renders", "v2")


def main():
    names = [a for a in sys.argv[1:] if a in V.VEHICLES] or list(V.VEHICLES)
    os.makedirs(OUT, exist_ok=True)
    for name in names:
        r, spec = V.build(name)
        clip = "Walk" if "Walk" in r.frames else ("Move" if "Move" in r.frames else "Idle")
        fr = r.frames[clip]
        r.pose_baked(fr[len(fr) // 3])
        bpy.context.view_layer.update()
        ev = r.mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        pts = [r.mesh.matrix_world @ Vector(c) for c in ev.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        size = max((hi - lo).length, 1.0)
        c = (lo + hi) / 2
        cam = stage.setup(1400, 1000, samples=24, wall=False)
        k = size / 2.2
        stage.studio((c.x, c.y, c.z), scale=k * k)
        for o in bpy.data.objects:
            if o.type == "LIGHT":
                o.location = c + (o.location - c) * k
        stage.aim(cam, c + Vector((-0.75, -1.25, 0.45)) * size * 1.15, c, 40)
        stage.render(os.path.join(OUT, f"Vehicle_{name}.png"))


main()
