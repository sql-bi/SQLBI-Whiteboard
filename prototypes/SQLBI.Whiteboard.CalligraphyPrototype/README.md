# SQLBI Whiteboard Calligraphy Lab

This isolated WPF prototype uses the same low-latency `DynamicRenderer` input path as SQLBI Whiteboard while exposing the calligraphy response parameters directly on the canvas.

Start with **Current app** to reproduce the current response, then compare **Stronger preset**. Adjust one parameter at a time and use **Copy settings** to copy the complete parameter set for later integration.

The most important controls are:

- **Pressure exponent**: values above 1 make light pressure substantially thinner.
- **Pressure influence**: controls how much physical pressure affects width.
- **Speed influence**: controls the maximum thinning caused by speed.
- **Speed reference**: lower values make speed thinning engage earlier.
- **Speed smoothing**: higher values make speed changes steadier but slower to react.
- **Minimum width**: prevents very light or fast strokes from disappearing.
- **Nib multipliers and angle**: control the broad-edge nib geometry.

Only pen input draws. Mouse and touch input are ignored by the canvas so they remain available for the tuning controls.
