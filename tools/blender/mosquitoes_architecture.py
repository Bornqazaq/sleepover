"""Closed apartment envelopes, inspired by contemporary Astana residential blocks.

Executed by mosquitoes_courtyard.py. Coordinates are Unity metres. Only the
existing room window is open; all other glazing sits over continuous backing.
"""
architecture_palette = {
    'FacadeCream': 'C6C0AE', 'FacadeSand': 'AC9780',
    'FacadeGraphite': '4C5159', 'Clinker': '92705B',
    'Roofing': '49555B', 'WindowReflection': '557483',
    'PlinthBasalt': '5A6268',
}
palette.update(architecture_palette)
for name, color in architecture_palette.items():
    rgb = tuple(int(color[i:i+2], 16)/255 for i in (0, 2, 4))
    material = bpy.data.materials.get('MSQ_'+name) or bpy.data.materials.new('MSQ_'+name)
    material.use_nodes = True
    material.diffuse_color = (*rgb, 1)
    shader = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value = (*rgb, 1)
    shader.inputs['Roughness'].default_value = .76
    mats[name] = material

floor_height = 3.6


def face_box(name, center, normal, u, y, outward, size, material):
    """A facade module in a face's local width / height / depth coordinates."""
    nx, nz = normal
    tx, tz = -nz, nx
    p = (center[0]+tx*u+nx*outward, y, center[1]+tz*u+nz*outward)
    s = (size[2] if nx else size[0], size[1], size[0] if nx else size[2])
    cb(name, p, s, material)


def window_module(center, normal, u, y, lit, loggia=False):
    put = lambda name, du, yy, depth, s, mat: face_box(name, center, normal, u+du, yy, depth, s, mat)
    # Recessed glazing with a solid dark reveal, full perimeter and projecting sill.
    put('Reveal', 0, y+1.97, .032, (2.30, 2.25, .12), 'FacadeGraphite')
    put('Glazing', 0, y+1.97, .108, (2.06, 1.99, .04), lit)
    for du in (-1.055, -.34, 1.055):
        put('WindowMullion', du, y+1.97, .155, (.055, 2.08, .09), 'Metal')
    for yy in (y+.935, y+3.005):
        put('WindowFrame', 0, yy, .155, (2.16, .055, .09), 'Metal')
    put('WindowTransom', 0, y+2.59, .16, (2.12, .035, .07), 'Metal')
    put('MetalDripSill', 0, y+.91, .205, (2.34, .10, .43), 'Stone')
    # A subdued reflected upper pane keeps unlit windows from reading as holes.
    if lit == 'WindowDark':
        put('UpperPaneReflection', .36, y+2.81, .133, (1.31, .32, .006), 'WindowReflection')
    if loggia:
        # Enclosed winter loggia: solid side cheeks, floor, roof and glazing.
        # Astana references use integrated glazing rather than open hanging trays.
        for du in (-1.24, 1.24):
            put('LoggiaCheek', du, y+1.83, .50, (.16, 3.30, 1.13), 'FacadeCream')
        for yy in (y+.21, y+3.45):
            put('LoggiaSlab', 0, yy, .54, (2.64, .18, 1.23), 'Stone')
        put('LoggiaSpandrel', 0, y+.66, 1.10, (2.40, .77, .12), 'FacadeGraphite')
        put('LoggiaGlass', 0, y+2.11, 1.105, (2.38, 2.00, .035), lit)
        for du in (-1.18, -.39, .39, 1.18):
            put('LoggiaMullion', du, y+2.10, 1.155, (.055, 2.04, .07), 'Metal')
        for yy in (y+1.095, y+3.115):
            put('LoggiaRail', 0, yy, 1.155, (2.43, .065, .08), 'Metal')


def facade(group, center, normal, width, floors, accent, hero=False):
    global chunk
    chunk = group
    height = floors*floor_height
    count = max(1, int(width/3))
    step = width/count
    # Host frontage is centred at z=.94, exactly matching the room aperture.
    for c in range(count):
        u = -width/2+(c+.5)*step
        zone = accent if c in (0, 1, count-2, count-1) else 'FacadeCream'
        if not (hero and c == 4):
            face_box('CladdingBay', center, normal, u, ground+height/2, .012,
                     (step-.025, height, .07), zone)
        # Narrow flush joints, not gaps in the building envelope.
        face_box('VerticalJoint', center, normal, u-step/2, ground+height/2, .052,
                 (.020, height, .016), 'FacadeGraphite')
        for f in range(floors):
            y = ground+f*floor_height
            if hero and c == 4 and f == 9:
                # Complete the facade around the only real opening. Inner edges
                # match Bedroom.fbx: z=-.08..1.96 and y=1.02..3.04.
                for du in (-1.265, 1.265):
                    face_box('HeroSideInfill', center, normal, u+du, 1.8, .02,
                             (.51, 3.60, .08), 'FacadeCream')
                for yy, hh in ((.51, 1.02), (3.32, .56)):
                    face_box('HeroInfill', center, normal, u, yy, .02,
                             (2.05, hh, .08), 'FacadeCream')
                continue
            if hero and c == 4:
                face_box('CentralCladding', center, normal, u, y+1.8, .012,
                         (step-.025, 3.6, .07), 'FacadeCream')
            # Ground level uses taller, darker storefront / entrance framing.
            if f == 0:
                face_box('GroundPier', center, normal, u-step/2+.14, y+1.72, .15,
                         (.27, 3.44, .25), 'PlinthBasalt')
            roll = rng.random()
            lit = 'WindowWarm' if roll < .19 else 'WindowCool' if roll < .235 else 'WindowDark'
            window_module(center, normal, u, y, lit,
                          loggia=f > 0 and c % 4 == 1 and not (hero and c == 4))
            if f > 0 and c % 3 == 0:
                # Ventilated air-conditioner basket, separate from the window.
                face_box('ACBasket', center, normal, u+1.02, y+.61, .31,
                         (.63, .62, .52), 'FacadeGraphite')
                for k in range(5):
                    face_box('BasketLouvre', center, normal, u+1.02, y+.38+k*.115, .585,
                             (.62, .025, .025), 'Metal')
    for f in range(floors+1):
        yy = ground+f*floor_height
        face_box('PanelHorizontalJoint', center, normal, 0, yy+.065, .065,
                 (width, .028, .025), 'FacadeGraphite')
        if f in (1, 2, floors-2, floors):
            face_box('ArchitecturalBelt', center, normal, 0, yy, .14,
                     (width+.12, .18, .30), 'FacadeCream')
    face_box('BaseCourse', center, normal, 0, ground+.26, .15,
             (width+.08, .52, .28), 'PlinthBasalt')


def entrance(group, center, normal, u=0):
    global chunk
    chunk = group
    put = lambda name, du, yy, dep, s, mat: face_box(name, center, normal, u+du, ground+yy, dep, s, mat)
    put('EntryPortal', 0, 1.65, .22, (3.45, 3.13, .45), 'FacadeGraphite')
    put('EntryGlazing', 0, 1.66, .47, (2.98, 2.78, .055), 'WindowWarm')
    for du in (-1.46, -.5, .5, 1.46):
        put('DoorFrame', du, 1.65, .51, (.075, 2.85, .085), 'Metal')
    for du in (-.44, .44):
        put('DoorPull', du, 1.45, .57, (.035, .54, .045), 'Chrome')
    put('EntryStep', 0, .12, 1.0, (4.1, .24, 2.35), 'Stone')
    put('EntryCanopy', 0, 3.26, 1.06, (4.1, .22, 2.42), 'FacadeGraphite')
    put('CanopyLight', 0, 3.137, 1.1, (3.20, .028, 1.60), 'Lantern')
    nx, nz = normal
    tx, tz = -nz, nx
    light_points.append(dict(position=[center[0]+tx*u+nx*1.5, ground+2.83, center[1]+tz*u+nz*1.5],
                             intensity=22, range=9, color=[1, .74, .47]))


def roof(group, cx, cz, depth, width, floors):
    global chunk
    chunk = group
    top = ground+floors*floor_height
    cb('RoofDeck', (cx, top+.04, cz), (depth+.08, .32, width+.08), 'Roofing')
    for x in (cx-depth/2, cx+depth/2):
        cb('Parapet', (x, top+.63, cz), (.27, 1.12, width+.28), 'FacadeCream')
        cb('ParapetCap', (x, top+1.22, cz), (.39, .085, width+.39), 'Stone')
    for z in (cz-width/2, cz+width/2):
        cb('Parapet', (cx, top+.63, z), (depth+.28, 1.12, .27), 'FacadeCream')
        cb('ParapetCap', (cx, top+1.22, z), (depth+.39, .085, .39), 'Stone')
    cb('LiftOverrun', (cx-1.0, top+1.5, cz), (4.5, 2.8, 5.6), 'FacadeGraphite')
    cb('PlantRoof', (cx-1.0, top+2.98, cz), (4.75, .16, 5.85), 'Stone')
    for z in (cz-width*.30, cz+width*.30):
        cb('VentilationDuct', (cx+1.8, top+.72, z), (1.25, 1.16, 1.9), 'Metal')
        cb('VentilationCap', (cx+1.8, top+1.35, z), (1.55, .12, 2.20), 'Roofing')


def tower(cx, cz, w, d, floors, accent, name=None):
    global chunk
    name = name or 'Building_%d_%d' % (cx, cz)
    chunk = name+'_Structure'
    h = floors*floor_height
    # All four sides and both end planes belong to one solid closed backing.
    cb('ClosedBody', (cx, ground+h/2, cz), (d, h, w), 'FacadeCream')
    cb('Podium', (cx, ground+.08, cz), (d+2.2, .30, w+2.2), 'Paving')
    for suffix, center, normal, width in (
        ('Front', (cx-d/2, cz), (-1, 0), w),
        ('Back', (cx+d/2, cz), (1, 0), w),
        ('South', (cx, cz-w/2), (0, -1), d),
        ('North', (cx, cz+w/2), (0, 1), d),
    ):
        facade(name+'_'+suffix, center, normal, width, floors, accent)
    roof(name+'_Roof', cx, cz, d, w, floors)
    entrance(name+'_Entry', (cx-d/2, cz), (-1, 0))


# The host is a hollow building around the existing playable room. Wall pieces
# overlap at corners; the roof and base extend across their full thickness.
host_cx, host_cz, host_d, host_w, host_floors = -2.66, .94, 13.4, 27.0, 22
front, back = 4.04, -9.36
height = host_floors*floor_height
chunk = 'Host_Structure'
for z in (host_cz-host_w/2, host_cz+host_w/2):
    cb('SideWall', (host_cx, ground+height/2, z), (host_d+.40, height+.15, .42), 'FacadeCream')
cb('RearWall', (back, ground+height/2, host_cz), (.42, height+.15, host_w+.42), 'FacadeCream')
cb('Foundation', (host_cx, ground-.14, host_cz), (host_d+.42, .55, host_w+.42), 'PlinthBasalt')
cb('PavementApron', (host_cx, ground+.045, host_cz), (host_d+2.3, .17, host_w+2.3), 'Paving')
# Solid frontage minus the exact functional room aperture.
for lo, hi in ((ground-.08, 1.02), (3.04, ground+height+.08)):
    cb('FrontWall', (front, (lo+hi)/2, host_cz), (.40, hi-lo, host_w+.42), 'FacadeCream')
for lo, hi in ((host_cz-host_w/2-.21, -.08), (1.96, host_cz+host_w/2+.21)):
    cb('FrontSideWall', (front, 2.03, (lo+hi)/2), (.40, 2.02, hi-lo), 'FacadeCream')
# The room's wall ends at x=3.84; reveals bridge to the exterior skin.
for z in (-.08, 1.96):
    cb('HeroReveal', (3.83, 2.03, z), (.46, 2.13, .085), 'Ivory')
for y in (1.02, 3.04):
    cb('HeroReveal', (3.83, y, .94), (.46, .085, 2.13), 'Ivory')
cb('HeroDripSill', (4.04, .963, .94), (.74, .09, 2.30), 'Stone')
facade('Host_Front', (front+.205, host_cz), (1, 0), host_w, host_floors, 'Clinker', hero=True)
facade('Host_Back', (back-.215, host_cz), (-1, 0), host_w, host_floors, 'Clinker')
for suffix, z, normal in (('South', host_cz-host_w/2-.215, (0, -1)),
                           ('North', host_cz+host_w/2+.215, (0, 1))):
    facade('Host_'+suffix, (host_cx, z), normal, host_d, host_floors, 'FacadeSand')
roof('Host_Roof', host_cx, host_cz, host_d+.2, host_w+.2, host_floors)
entrance('Host_Entry', (front+.205, host_cz), (1, 0), u=-6)

# Different heights and linked volumes give a residential quarter silhouette.
tower(-7, 23, 18, 24, 16, 'Clinker', 'Host_AttachedWing')
tower(46, -28, 24, 13, 22, 'Clinker')
tower(55, 31, 21, 15, 18, 'FacadeSand')
tower(120, -8, 30, 16, 12, 'FacadeGraphite')
tower(-68, -39, 27, 16, 16, 'Clinker', 'RearQuarter_West')
tower(-81, 40, 33, 18, 12, 'FacadeSand', 'RearQuarter_East')
tower(29, -93, 42, 18, 10, 'Clinker', 'SouthQuarter')
tower(84, 99, 36, 16, 14, 'FacadeGraphite', 'NorthQuarter')
