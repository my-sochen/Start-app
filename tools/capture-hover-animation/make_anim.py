import math
import sys

# Start-app beta: capture-button hover animation, single motion model.
# Subcommands:
#   make_anim.py svg          -> writes capture-hover.svg (SMIL, sampled to match frames)
#   make_anim.py frame <t>    -> frame SVG at time t seconds on stdout
#   make_anim.py ship         -> standalone ship SVG (logo line-art trace, no beam) on stdout
#
# Composition (owner Nov21 22:31): the logo's own layout - the traced logo ship sits at
# the BOTTOM and two wavy SOLID streams pour from above into its hatch slot (owner 23:03:
# lines not dashes, separated endpoints). The wave travels down the lines into the slot;
# boxes descend straight and dissolve into the hatch. The whole icon gently bobs and tilts;
# the hull dots pulse. Seamless 1.8s loop. All motion inside the button bounds.

T = 1.8
NAVY = '#142743'

SHIP_SCALE = 0.216
SHIP_TY = 40.0                         # top of ship in 64-space (ship sits at the bottom, logo layout)
SHIP_TX = 32 - 119 * SHIP_SCALE        # centers logo-x 119 on view-x 32
# (owner 23:32 rejected the pixel-traced body - "תחזיר את הגרסה האחרונה"; trace_body.py /
# ship_body.py kept in the folder but unused. His actual ask: a subtle beam-style glow on
# the hand-drawn body - GLOW below.)

# beam streams in 64-space: the logo's wavy stream shape, hung from the bowl's underside.
# Centerline = splay from bowl to stream foot + a perpendicular sine wiggle (like the logo's
# meandering streams), mirrored between the two sides.
# ship at the bottom (logo layout); gentler, wider wave per owner 22:31 ("התלתול צריך להיות פחות חד").
# owner 23:03: the two streams must NOT meet - each ends at its own distinct point with
# clear space between them (endpoints 6px apart, each over its own half of the hatch slot),
# and the beam is two SOLID lines, not dashes. The pour-into-the-hole feel comes from the
# wave itself traveling down the lines into the slot (phase animates), plus the glow/dimming.
STREAM_LEFT = dict(p0=(16, 9), p1=(28.8, 43.0), amp=2.0)
STREAM_RIGHT = dict(p0=(48, 9), p1=(34.8, 43.0), amp=-2.0)
WIGGLE_CYCLES = 1.25


def stream_point(st, s, t=0.0):
    dx, dy = st['p1'][0] - st['p0'][0], st['p1'][1] - st['p0'][1]
    L = math.hypot(dx, dy)
    nx, ny = -dy / L, dx / L
    # sin(pi*s) envelope: wiggle dies out at both ends so the stream attaches exactly.
    # phase -2pi*t/T: the wave travels toward s=1, i.e. DOWN into the hatch; one full
    # spatial period per loop (cycles*0.8=1), so the loop is seamless.
    w = st['amp'] * math.sin(2 * math.pi * WIGGLE_CYCLES * s - 2 * math.pi * t / T) * math.sin(math.pi * s)
    return (st['p0'][0] + dx * s + nx * w, st['p0'][1] + dy * s + ny * w)


def stream_path(st, t=0.0, samples=9):
    # Catmull-Rom through sampled centerline points -> smooth cubic path
    pts = [stream_point(st, i / (samples - 1), t) for i in range(samples)]
    d = ['M%.2f %.2f' % pts[0]]
    for i in range(samples - 1):
        p_prev = pts[max(i - 1, 0)]
        p0, p1 = pts[i], pts[i + 1]
        p_next = pts[min(i + 2, samples - 1)]
        c1 = (p0[0] + (p1[0] - p_prev[0]) / 6.0, p0[1] + (p1[1] - p_prev[1]) / 6.0)
        c2 = (p1[0] - (p_next[0] - p0[0]) / 6.0, p1[1] - (p_next[1] - p0[1]) / 6.0)
        d.append('C%.2f %.2f %.2f %.2f %.2f %.2f' % (c1 + c2 + p1))
    return ' '.join(d)
# boxes descend on STRAIGHT, uniform paths into the hatch (owner 22:32: "במסלול אחיד ללמטה"),
# not following the wavy streams; the wavy dots flow around them. Slight inward diagonal,
# like the boxes floating between the streams in the logo.
BOXES = [dict(p_top=(22, 9), p_end=(31, 44), t0=0.12, d=0.85),
         dict(p_top=(42, 9), p_end=(33, 44), t0=0.72, d=0.85)]

BOB_AMP = 2.0          # px; float-up profile from the owner's flaticon motion reference
TILT_AMP = 1.0         # degrees, phase-shifted against the bob
TILT_CX, TILT_CY = 32, 40

# Hull dots pixel-measured from ufo.png (owner 23:03, approved 23:16 - kept exactly as
# approved): logo solid navy dots d=14 at left (80.8,175.6), center (127.6,182.9), right
# (175.2,174.5): a shallow arc, center dot lower, hanging at/below the rim's bottom edge.
DOTS = ((72.5, 41.8, 7), (119.1, 47.6, 7), (166.5, 41.0, 7))
HATCH = dict(cx=118, cy=25, rx=25, ry=8, sw=7)
# subtle body glow (owner 23:32: "תעשה את הגוף זוהר כזה כמו הקרן רק פחות מודגש"): same
# treatment as the beam glow (wider low-opacity stroke underlay) but weaker - beam glow is
# 1.77x width at 0.18 opacity; body glow is ~1.6x width at 0.10.
GLOW_SW = 15
GLOW_OP = 0.14
BODY_SHAPES = ('<ellipse cx="119" cy="34" rx="106" ry="31"/>'
               '<path d="M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56"/>')


def ship_inner(dot_anims=None):
    # dot_anims: None -> static dots; else list of 3 '<animate .../>' strings, one per hull dot
    dots = ''
    for (cx, cy, r), anim in zip(DOTS, dot_anims or (None, None, None)):
        if anim:
            dots += '<circle cx="%s" cy="%s" r="%s" opacity="0.86">%s</circle>' % (cx, cy, r, anim)
        else:
            dots += '<circle cx="%s" cy="%s" r="%s"/>' % (cx, cy, r)
    return ('<g fill="none" stroke="%(c)s" stroke-width="%(gsw)s" opacity="%(gop)s">%(shapes)s</g>'
            '<g fill="none" stroke="%(c)s" stroke-width="9.5">'
            '<ellipse cx="119" cy="34" rx="106" ry="31"/>'
            '<path d="M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56" stroke-width="9"/>'
            '</g>'
            '<ellipse cx="%(hcx)s" cy="%(hcy)s" rx="%(hrx)s" ry="%(hry)s" fill="#ffffff" stroke="%(c)s" stroke-width="%(hsw)s"/>'
            '<g fill="%(c)s">' + dots + '</g>') % dict(c=NAVY, gsw=GLOW_SW, gop=GLOW_OP, shapes=BODY_SHAPES,
                                                     hcx=HATCH['cx'], hcy=HATCH['cy'],
                                                     hrx=HATCH['rx'], hry=HATCH['ry'], hsw=HATCH['sw'])


def ship_inner_ops(ops):
    # static ship with explicit per-dot opacities (frame rendering)
    dots = ''.join('<circle cx="%s" cy="%s" r="%s" opacity="%s"/>' % (cx, cy, r, op)
                   for (cx, cy, r), op in zip(DOTS, ops))
    return ('<g fill="none" stroke="%(c)s" stroke-width="%(gsw)s" opacity="%(gop)s">%(shapes)s</g>'
            '<g fill="none" stroke="%(c)s" stroke-width="9.5">'
            '<ellipse cx="119" cy="34" rx="106" ry="31"/>'
            '<path d="M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56" stroke-width="9"/>'
            '</g>'
            '<ellipse cx="%(hcx)s" cy="%(hcy)s" rx="%(hrx)s" ry="%(hry)s" fill="#ffffff" stroke="%(c)s" stroke-width="%(hsw)s"/>'
            '<g fill="%(c)s">' + dots + '</g>') % dict(c=NAVY, gsw=GLOW_SW, gop=GLOW_OP, shapes=BODY_SHAPES,
                                                     hcx=HATCH['cx'], hcy=HATCH['cy'],
                                                     hrx=HATCH['rx'], hry=HATCH['ry'], hsw=HATCH['sw'])


def smooth(a, b, x):
    if x <= a:
        return 0.0
    if x >= b:
        return 1.0
    f = (x - a) / (b - a)
    return f * f * (3 - 2 * f)


def bez(pts, u):
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = pts
    v = 1 - u
    x = v*v*v*x0 + 3*v*v*u*x1 + 3*v*u*u*x2 + u*u*u*x3
    y = v*v*v*y0 + 3*v*v*u*y1 + 3*v*u*u*y2 + u*u*u*y3
    return x, y


def bez_multi(segs, u):
    # piecewise cubic: u in [0,1] split evenly across segments
    u = max(0.0, min(1.0, u))
    n = len(segs)
    i = min(int(u * n), n - 1)
    return bez(segs[i], u * n - i)


def float_profile(t):
    # reference motion language (flaticon ufo_18166384): rest at the bottom, ease up,
    # hold briefly, long easy descent back to rest. 0..1..0 over the loop, seamless.
    u = (t % T) / T
    return smooth(0.05, 0.42, u) - smooth(0.55, 0.95, u)


def bob_dy(t):
    return -BOB_AMP * float_profile(t)   # negative y: the ship floats UP


def tilt_deg(t):
    return TILT_AMP * math.sin(2 * math.pi * t / T + 1.2)


def dot_op(t, phase):
    return 0.72 + 0.28 * (0.5 + 0.5 * math.sin(4 * math.pi * t / T + phase))


def beam_op(t):
    # like the reference beam: strongest while the ship rests, retracts/dims while it floats up
    base = 0.85 * smooth(0.03, 0.25, t) * (1 - smooth(1.58, 1.78, t))
    return base * (1 - 0.35 * float_profile(t))


def box_state(t, b):
    # returns (opacity, x, y, scale) in 64-space
    p = (t - b['t0']) / b['d']
    if p <= 0:
        return 0.0, b['p_top'][0], b['p_top'][1], 1.0
    if p >= 1:
        return 0.0, b['p_end'][0], b['p_end'][1], 0.15
    # straight uniform descent: constant speed, linear interpolation
    x = b['p_top'][0] + (b['p_end'][0] - b['p_top'][0]) * p
    y = b['p_top'][1] + (b['p_end'][1] - b['p_top'][1]) * p
    sc = 1 - 0.85 * p
    op = smooth(0.0, 0.10, p) * (1 - smooth(0.78, 0.99, p))
    return op, x, y, sc


def ship_group(inner):
    return ('<g transform="translate(%.3f %.3f) scale(%.4f)">%s</g>'
            % (SHIP_TX, SHIP_TY, SHIP_SCALE, inner))


def beams_svg(op, t):
    # two SOLID wavy lines (owner 23:03); the wave travels down into the hatch
    left = stream_path(STREAM_LEFT, t)
    right = stream_path(STREAM_RIGHT, t)
    return ('<g fill="none" stroke="%s" stroke-linecap="round" opacity="%.3f">'
            '<g stroke-width="4.6" opacity="0.18"><path d="%s"/><path d="%s"/></g>'
            '<g stroke-width="2.6"><path d="%s"/><path d="%s"/></g></g>'
            % (NAVY, op, left, right, left, right))


def box_svg(op, x, y, sc):
    return ('<g opacity="%.3f"><g transform="translate(%.2f %.2f)"><g transform="scale(%.3f)">'
            '<rect x="-5" y="-5" width="10" height="10" rx="1.8" fill="#ffffff" '
            'stroke="%s" stroke-width="1.7"/></g></g></g>' % (op, x, y, sc, NAVY))


def frame_svg(t, width=256):
    t = t % T
    ops = ['%.3f' % dot_op(t, ph) for ph in (0.0, 2.1, 4.2)]
    parts = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="%d" height="%d">' % (width, width),
             '<rect width="64" height="64" rx="10" fill="#ffffff"/>',
             '<g transform="translate(0 %.3f)">' % bob_dy(t),
             '<g transform="rotate(%.3f %s %s)">' % (tilt_deg(t), TILT_CX, TILT_CY),
             beams_svg(beam_op(t), t)]
    for b in BOXES:
        op, x, y, sc = box_state(t, b)
        parts.append(box_svg(op, x, y, sc))
    parts.append(ship_group(ship_inner_ops(ops)))
    parts.append('</g></g></svg>')
    return ''.join(parts)


def sampled(values):
    n = len(values)
    kt = ';'.join('%.4f' % (i / (n - 1)) for i in range(n))
    return ';'.join(values), kt


def smil_svg():
    N = 37  # samples per loop, 50ms apart
    ts = [T * i / (N - 1) for i in range(N)]

    bob_vals, bob_kt = sampled(['0 %.3f' % bob_dy(t) for t in ts])
    tilt_vals, tilt_kt = sampled(['%.3f %s %s' % (tilt_deg(t), TILT_CX, TILT_CY) for t in ts])
    beam_vals, beam_kt = sampled(['%.3f' % beam_op(t) for t in ts])
    left_vals, _ = sampled([stream_path(STREAM_LEFT, t) for t in ts])
    right_vals, _ = sampled([stream_path(STREAM_RIGHT, t) for t in ts])

    dot_anims = []
    for phase in (0.0, 2.1, 4.2):
        vals, kt = sampled(['%.3f' % dot_op(t, phase) for t in ts])
        dot_anims.append('<animate attributeName="opacity" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>' % (vals, kt))

    out = ['''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
  <!-- Start-app beta: capture-button hover animation. The whole icon moves: the logo ship
       (traced line art, no erasure stub) gently bobs and tilts, its hull dots pulse (positions
       pixel-measured from the logo), two solid wavy beam lines pour down into the hatch slot,
       and boxes descend straight into the hatch. Seamless 1.8s loop.
       All motion inside the button bounds. Editable source; convert to Lottie/XAML later if
       the app needs another format. Generated by assets/make_anim.py (same motion model as
       the previews); pure vector, no external images. -->
  <g>
    <animateTransform attributeName="transform" type="translate" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
  <g>
    <animateTransform attributeName="transform" type="rotate" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
  <!-- beam streams from the underside: two solid wavy lines, the wave travels down
       into the hatch slot (pour feel), lines dim while the ship floats -->
  <g fill="none" stroke="%s" stroke-linecap="round" opacity="0">
    <animate attributeName="opacity" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
    <g stroke-width="4.6" opacity="0.18">
      <path d="%s"><animate attributeName="d" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/></path>
      <path d="%s"><animate attributeName="d" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/></path>
    </g>
    <g stroke-width="2.6">
      <path d="%s"><animate attributeName="d" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/></path>
      <path d="%s"><animate attributeName="d" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/></path>
    </g>
  </g>
''' % (bob_vals, bob_kt, tilt_vals, tilt_kt, NAVY, beam_vals, beam_kt,
       stream_path(STREAM_LEFT), left_vals, beam_kt,
       stream_path(STREAM_RIGHT), right_vals, beam_kt,
       stream_path(STREAM_LEFT), left_vals, beam_kt,
       stream_path(STREAM_RIGHT), right_vals, beam_kt)]

    for i, b in enumerate(BOXES):
        ops, trs, scs = [], [], []
        for t in ts:
            op, x, y, sc = box_state(t, b)
            ops.append('%.3f' % op)
            trs.append('%.2f %.2f' % (x, y))
            scs.append('%.3f' % sc)
        op_vals, kt = sampled(ops)
        out.append('''  <!-- box %d: sucked up the %s stream into the bowl -->
  <g opacity="0">
    <animate attributeName="opacity" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
    <animateTransform attributeName="transform" type="translate" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
    <g>
      <animateTransform attributeName="transform" type="scale" values="%s" keyTimes="%s" dur="1.8s" repeatCount="indefinite"/>
      <rect x="-5" y="-5" width="10" height="10" rx="1.8" fill="#ffffff" stroke="%s" stroke-width="1.7"/>
    </g>
  </g>
''' % (i + 1, 'left' if i == 0 else 'right', op_vals, kt,
       ';'.join(trs), kt, ';'.join(scs), kt, NAVY))

    out.append('''  <!-- the logo ship, traced line art -->
  %s
''' % ship_group(ship_inner(dot_anims)))

    out.append('</g></g></svg>\n')
    return ''.join(out)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'svg':
        with open('capture-hover.svg', 'w') as f:
            f.write(smil_svg())
        print('capture-hover.svg written')
    elif cmd == 'frame':
        print(frame_svg(float(sys.argv[2])))
    elif cmd == 'ship':
        print('<svg xmlns="http://www.w3.org/2000/svg" viewBox="10 0 217 102" width="217" height="102">'
              + ship_inner() + '</svg>')
    else:
        sys.exit('usage: make_anim.py svg | frame <t> | ship')
