# oculix (Node.js / TypeScript)

Node.js wrapper for [OculiX](https://github.com/oculix-org/Oculix) — visual automation for the real world.

## Install

```bash
npm install oculix
```

Requirements: Node.js 18+, Java 11+ on `PATH` (Eclipse Temurin or Azul Zulu).

The OculiX engine (~160 MB fat JAR) is downloaded on first use into `~/.oculix/lib/`.

## Quickstart

```javascript
const { Screen, App, VNCScreen } = require('oculix');

(async () => {
  const screen = await Screen.create();
  await screen.click('login.png');
  await screen.type('admin');
  await screen.click('submit.png');
  await screen.wait('dashboard.png', 10);

  const calc = await App.open('calc');
  await screen.click('button_7.png');

  const vnc = await VNCScreen.start('10.0.0.42', 5900, '', 1920, 1080);
  await vnc.click('logo.png');
  await vnc.stop();
})();
```

## With Playwright (hybrid browser + desktop)

```javascript
const { chromium } = require('playwright');
const { Screen } = require('oculix');

const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto('https://app.example.com/export');
await page.click('#export-pdf');

// Browser triggered a native save dialog — drive it with OculiX
const screen = await Screen.create();
await screen.wait('save_dialog.png', 10);
await screen.type('report.pdf');
await screen.click('save_button.png');

await browser.close();
```

## License

MIT
