# CWI Marketing + UI Asset Pack

This folder contains a ready-to-use **brand + UI kit** derived from the visual language in the screenshots you provided (deep navy gradients, soft neumorphism, vivid azure accent).

## What’s inside

- **Brand guidelines PDF**: `01_Brand_Guidelines/CWI_Brand_Guidelines.pdf`
- **Design tokens** (single source of truth): `02_Tokens/tokens.json`
  - Web CSS variables: `02_Tokens/tokens.css`
  - TypeScript constants: `02_Tokens/tokens.ts`
  - iOS Swift (UIKit + SwiftUI): `02_Tokens/tokens.swift`
  - Android colors.xml: `02_Tokens/colors.xml`
- **Icons**:
  - Web-ready SVGs (use `currentColor`): `04_Icons/svg/`
  - PNG exports: `04_Icons/png/24/` and `04_Icons/png/48/`
- **App Icons**:
  - iOS asset catalog: `05_App_Icons/iOS/AppIcon.appiconset/`
  - Android mipmap folders + adaptive parts: `05_App_Icons/Android/`
- **Favicons + manifest**: `06_Favicons/`
- **Backgrounds + marketing images**: `07_Images/`
- **Reference screenshots** (per-app): `08_Reference/<app>/`

## Fast integration

### Web
1. Copy `02_Tokens/tokens.css` into your project and import it globally.
2. Use the SVG icons from `04_Icons/svg`:
   ```html
   <img src="/icons/search.svg" style="color: var(--cwi-accent-500);" />
   ```
   If you’re using inline SVG, set `color` on the container and the icon will inherit it.
3. Use `07_Images/Backgrounds/bg_radial_1920x1080.png` (or other sizes) for hero sections / splash screens.

### iOS
1. Drag `05_App_Icons/iOS/AppIcon.appiconset` into your Xcode asset catalog.
2. Add `02_Tokens/tokens.swift` to your project and reference `Color.cwi_navy_850`, `Color.cwi_accent_500`, etc.

### Android
1. Copy the `mipmap-*` folders under `05_App_Icons/Android/` into `app/src/main/res/`.
2. Merge `02_Tokens/colors.xml` into your `res/values/colors.xml`.
3. (Optional) Use `ic_launcher_foreground_432.png` + `ic_launcher_background_432.png` to define an adaptive launcher icon.

## Notes / assumptions
- The screenshots contain inconsistent lettering (e.g., **CWI/COI**). This kit standardizes on **CWI**. If your actual brand mark differs, swap in your final logo while keeping the background/color treatments.
- The app icon included here is a **generated monogram** intended as a clean default. Replace the letters with your final logo mark if available.


## Reference organization

Use `08_Reference/general/` for raw or shared captures, and app-specific folders for UI evidence you want tracked in git (e.g., `08_Reference/cwi-glassbox-web-ui/`, `08_Reference/cwi-ios-carplay/`).
