# Third-Party Licenses

CSFFModFramework is released under the MIT License (see `LICENSE`).

## LitJSON-Compatible In-Tree Implementation

**File:** `LitJSON.dll` (deployed alongside the framework)
**Source code:** `CSFFModFramework/Stubs/LitJson/LitJsonStub.cs` (in this repo)

The `LitJSON.dll` shipped with this framework is not a third-party LitJSON binary. It is built from `LitJsonStub.cs` in this repository: a minimal in-tree implementation of the LitJSON v0.18.0.0 public API sufficient for the framework's internal use and for resolving CLR imports of mods built against the upstream library.

The upstream LitJSON project by Leonardo Taglialegne / Lupus, et al. is released into the public domain. This in-tree implementation is covered by the framework's MIT license (see `LICENSE`).

## UnityGifDecoder

**File:** `UnityGifDecoder.dll` (deployed alongside the framework)
**Project:** 3DI70R/Unity-GifDecoder v1.0.3

UnityGifDecoder is redistributed under the MIT License:

```text
MIT License

Copyright (c) 2020 3DI70R

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Build-Time References Not Redistributed

The following assemblies appear in `lib/` for compilation only and are not redistributed by this framework. Users obtain them by installing the game, Unity runtime files, or BepInEx through their normal channels:

- BepInEx 5.x - BepInEx contributors (LGPL-2.1)
- HarmonyX (`0Harmony.dll`) - BepInEx contributors / Harmony contributors (MIT)
- Unity engine assemblies (`UnityEngine.*.dll`) - Unity Technologies
- Game assemblies (`Assembly-CSharp.dll`, etc.) - WinterSpring Games

Because these files are not bundled with the deployed plugin folder, no attribution copy is required to ride along with our distribution.
