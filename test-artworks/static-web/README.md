# ChromaFlow: Harmonic Generative Vector Field

**ExhibitOS Test Artwork: Static Web**

An interactive, mathematical canvas flowfield simulation designed for continuous exhibition kiosk display.

## Characteristics
- **Zero Dependencies**: Pure vanilla JavaScript, HTML5 Canvas, and modern CSS. No npm packages or external CDN assets. Runs offline.
- **Dynamic Color Palettes**: Slowly morphs between vibrant ambient palettes (Nebula, Solar, Abyssal, Cyberpunk).
- **Interactive**: Mouse, touch, and pointer events trigger gravitational vortices and shockwaves.
- **Exhibition Kiosk Ready**: Zero scrollbars, no text selection, automatic resize handling, and an auto-fading non-intrusive HUD with telemetry (FPS, uptime, particle count).

## Keyboard Controls
- `[H]`: Toggle HUD visibility
- `[C]`: Manually cycle color palette
- `[R]`: Clear canvas and reset field
- `[Space]`: Pause / Resume animation

## ExhibitOS Integration
Point **ExhibitOS Manager** to this folder (`test-artworks/static-web/`) and select **Website**. ExhibitOS detects `index.html` as the entry point and automatically serves it via its bundled static web server in Microsoft Edge fullscreen kiosk mode.
