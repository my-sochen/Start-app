# Capture button hover animation, blue UFO icon (design handoff, reference only)

Files: `start-app-capture-hover-blue-ufo-source.html` (reference implementation, exact numbers live in `setIcon()` and `icon()`), `start-app-capture-hover-blue-ufo-preview.mp4` (approved preview). Icon geometry: `start-app-trace-ufo-blue.svg`. Do not redraw or simplify the paths. The preview mock (zoomed bar, cursor, 1x inset) is not part of the product.

## Button
- Button 44x44 px, `overflow:hidden`, border-radius 4. Icon 34x34 px centered, svg `overflow:visible`; everything must stay inside the button (glow included).
- Rest: static icon, white background, border #c5cad4.
- Hover: background #eaf0fb, border #315cc5, animation runs. Leave: eases back to rest.

## SVG structure (viewBox `190 190 874 874`)
The traced path has 8 subpaths: 0-3 = saucer + both S-beams + holes (one contour), 4-5 = small cube (outline + hole), 6-7 = big cube (outline + hole). Use `fill-rule="evenodd"`. Fill: linear gradient userSpaceOnUse (520,992) to (733,264), #1d4ed8 (0) to #7dd3fc (1).
Layers, bottom to top inside group `all` (the group that lifts and tilts):
1. `beam`: subpaths 0-3, masked by a vertical gradient mask (white at y=600, black at y=690). Contains `flow`: same path filled with the flow gradient (below).
2. `cubes`: clip rect y < 662 (the opening rim). Two paths: `k1` (subpaths 6+7, x 590-776, y 249-431), `k2` (subpaths 4+5, x 520-633, y 486-600), same gradient fill.
3. `body`: subpaths 0-3 masked by the inverse mask (black at y=600, white at y=690). The two masks cross-fade so there is no visible seam.

## Timing (one loop = 2.6 s, starts at hover, loops while hovered)
Let `f = 0.5 - 0.5*cos(2*pi*t/2.6)` and `e` = hover envelope. `e` rises with smoothstep over 0.35 s on enter and falls with smoothstep over 0.5 s on leave. All effects are multiplied by `e`, so enter and leave are smooth.
- Lift (whole icon): translateY = -46 * f * e (svg units, about 1.8 px at 34 px).
- Tilt (whole icon): rotate(2.4deg * sin(2*pi*t/2.6) * e) around (627, 760).
- Beam fade: beam opacity = 1 - 0.18 * f * e (weaker while the saucer is up, full at rest).
- Flow along the S-beams: linear gradient userSpaceOnUse, `spreadMethod=repeat`, from y=250 to y=690, stops: 0 #bae6fd opacity 0, 0.5 #bae6fd opacity 0.75, 1 #bae6fd opacity 0. Translate Y by `((t/1.3) % 1) * 440` (period 1.3 s, moving down toward the opening). The flow path opacity = 0.6 * e.
- Cubes, straight vertical fall (not following the S curves). Each cube loops with period 2.6 s; cube k1 phase 0, cube k2 phase 0.5. u = (t/2.6 + phase) % 1. translateY = e * (start + (end - start) * u), linear, no easing.
  - k1: start -34, end 417. k2: start -271, end 180. (start puts the cube at y=215, the top of the icon; end moves its top 4 units past the rim at y=662.)
  - opacity = 1 - e*(1 - smoothstep(u/0.12)) (fade in over the first 12 percent of the fall). At rest, offset is 0 and opacity 1, so the cubes sit in the icon positions.
  - The `cubes` clip at y=662 hides each cube as it enters the opening.
- Glow (CSS `filter: drop-shadow(0 0 R rgba(56,130,246,A))`, R in svg units, so it scales with the icon):
  - beam layer: R = 20*e, A = 0.6*e.
  - body layer: R = 22*e, A = 0.4*e*(0.8 + 0.2*sin(2*pi*t/2.6*2)) (gentle pulse, always weaker than the beam).
  - glow color: rgb(56,130,246) (#3882f6). This is my pick next to the icon blue, not an approved palette color.

## Implementation notes
- Drive with CSS keyframes or WPF/WinUI storyboards using the numbers above, `ease`: linear for cube fall and flow, sine for lift/tilt. Loop only while hovered, return to the rest pose on leave in 0.5 s. Provide a reduced-motion setting that shows the static rest icon.
- Not mapped from the earlier approved animation (the new icon does not contain them): the three flowing dots (the icon has two cubes), blinking body dots. Do not add them.
- The opening is a narrow hollow dome strip, not a tall circle; the cubes vanish at its upper edge.
- At the real 34 px size the lines are thin and the cubes small. The animation is readable mainly through motion and glow.
