const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
  const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  const base = process.env.SITE_URL || 'http://localhost:5201';
  await page.goto(base);
  await page.waitForSelector('.now-title');
  await page.waitForTimeout(1500);
  await page.locator('.volume input').evaluate(input => { input.value = '0'; input.dispatchEvent(new Event('input', { bubbles: true })); });
  await page.waitForFunction(() => document.getElementById('station-audio').volume === 0);
  await page.locator('#station-audio').evaluate(audio => { audio.dataset.identity = 'same-player'; });
  await page.getByRole('link', { name: 'Search library' }).click();
  await page.getByRole('heading', { name: 'The music library.' }).waitFor();
  assert.equal(await page.locator('#station-audio').getAttribute('data-identity'), 'same-player');

  async function search(term) {
    await page.getByLabel('Search songs and artists').fill(term);
    await page.getByRole('button', { name: /Search/ }).click();
    await page.waitForURL(url => url.searchParams.get('q') === term);
    await page.waitForTimeout(200);
  }
  await search('queen');
  assert.equal(await page.locator('.library-results .track-row').count(), 2);
  assert.equal(await page.locator('.artist-card').count(), 1);
  await page.locator('.track-title').filter({ hasText: 'Radio Ga Ga' }).click();
  await page.getByRole('heading', { name: 'Radio Ga Ga', exact: true }).waitFor();
  assert.match(await page.locator('.song-facts').innerText(), /Roger Taylor/);
  assert.equal(await page.locator('.detail img').getAttribute('src'), '/images/station.png');
  await page.locator('.detail a').filter({ hasText: 'Queen' }).click();
  await page.getByRole('heading', { name: 'Queen', exact: true }).waitFor();
  assert.match(await page.locator('.prose').innerText(), /2 tracks across 2 albums/);
  assert.equal(await page.locator('.track-row').count(), 2);

  await page.goto(base + '/library');
  await page.waitForTimeout(1000);
  await search('%');
  assert.equal(await page.locator('.track-title').count(), 1, 'Percent is literal');
  await search("Guns N' Roses");
  assert.equal(await page.locator('.track-title').count(), 1, 'Apostrophes are safe');
  await search('Hidden advert');
  assert.equal(await page.locator('.track-title').count(), 0, 'Exclude nonmusic');
  await search("' OR 1=1 --");
  assert.equal(await page.locator('.track-title').count(), 0, 'Parameterized search');
  await page.goto(base + '/songs/999999');
  assert.ok(await page.getByText('This entry could not be found in the library.').isVisible());

  await page.goto(base + '/recently-played');
  await page.locator('.track-row').first().waitFor();
  const recent = await page.locator('.track-row').count();
  assert.ok(recent > 1 && recent <= 11);
  if (process.env.CHECK_BROADCAST_MATCH === '1') {
    assert.ok(await page.locator('a[href="/songs/1000"]').count() > 0, 'Match beyond first 100 songs');
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(base);
  await page.waitForSelector('.now-title');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  assert.ok(await page.getByLabel('Volume', { exact: true }).isVisible());
  assert.ok(await page.getByRole('link', { name: 'Full player' }).isVisible());
  if (process.env.SCREENSHOT_DIR) {
    await page.screenshot({ path: process.env.SCREENSHOT_DIR + '/aneed-mobile.png', fullPage: true });
    await page.setViewportSize({ width: 1440, height: 1050 });
    await page.screenshot({ path: process.env.SCREENSHOT_DIR + '/aneed-desktop.png', fullPage: true });
  }
  assert.deepEqual(errors, []);
  console.log('PASS: SAM search, safe inputs, song/artist pages, artwork fallback, Live365 history, persistent audio element, mobile controls, no page errors.');
  await browser.close();
})().catch(error => { console.error(error); process.exit(1); });
