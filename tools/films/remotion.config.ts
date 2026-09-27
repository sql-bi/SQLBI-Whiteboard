// Settings for `npx remotion studio`. The renders in scripts/render.mjs pass their own
// options to the Node API and do not read this file.
import {Config} from '@remotion/cli/config';

Config.setVideoImageFormat('png');
Config.setPixelFormat('yuv420p');
