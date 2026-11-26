# Start-app beta - capture button hover animation: engineering handoff spec
Deliverable: capture-hover.svg (editable pure-vector SMIL, drop-in). Generator: make_anim.py
(subcommands: svg / frame <t> / ship) if engineering needs to re-render or tweak parameters.
Reference renders: capture-hover-preview.mp4 / .gif. Rest-state icon context: home-f1.png.
All animation stays inside the button bounds (verified per frame, min margin 1.5 button px).

## FINAL - capture hover animation approved, owner Nov22 00:14:47 ("מעולה זה טוב.", verified in observations)
- Frozen in assets/final/: capture-hover.svg (editable pure-vector SMIL, md5 3c590b42), capture-hover-preview.mp4 (8db0d007), .gif (f0b48389), make_anim.py generator snapshot (44c4bc17), home-f1.png (4a860e22).
- Final approved spec: rim ellipse cx119 cy34 rx106 ry31 sw9.5; bowl "M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56" sw9 (rounded bottom, no V); hatch window cx118 cy25 rx25 ry8 sw7 white fill; dots (72.5,41.8)/(119.1,47.6)/(166.5,41.0) r7 pulsing; body glow sw15 op0.14, beam glow 4.6 op0.18; two solid wavy beams (amp 2.0/-2.0, 1.25 cycles, wave travels down); boxes descend straight + dissolve; bob 2px float profile, tilt 1deg phase 1.2; seamless T=1.8s 60fps; min margin 1.5 button px.
- No code changes to the app; awaiting owner's answer on beta implementation (main asked).
