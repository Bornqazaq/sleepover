"""Keep authored smooth surfaces but remove tessellation invisible at mosquito camera distance."""
budgets={'Sneaker_':14000,'RumpledDuvet':16000,'FoldedThrow':16000,'DrapedHoodie':14000,'WardrobeGarment':9000,'Pillow':12000,'Backpack':16000,'DeskChair':16000}
deps=bpy.context.evaluated_depsgraph_get()
for ob in list(scene.objects):
    if ob.type!='MESH':continue
    target=next((budget for prefix,budget in budgets.items() if ob.name.startswith(prefix)),None)
    if target is None or ob.modifiers.get('Game surface budget'):continue
    ev=ob.evaluated_get(deps);mesh=ev.to_mesh();mesh.calc_loop_triangles();count=len(mesh.loop_triangles);ev.to_mesh_clear()
    if count>target:
        m=ob.modifiers.new('Game surface budget','DECIMATE');m.ratio=target/count
