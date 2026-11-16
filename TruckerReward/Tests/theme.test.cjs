const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
const source = readFileSync(join(__dirname, '../FrontEnd/wwwroot/js/theme.js'), 'utf8');
function load(storage) {
    const document = { documentElement: { dataset: {} } };
    const events = {};
    const window = { addEventListener: (name, callback) => events[name] = callback };
    runInNewContext(source, { document, window, localStorage: storage });
    return { theme: window.truckerTheme, document, events };
}
test('theme switches immediately and survives a new page load in both directions', () => {
    const values = new Map();
    const storage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) };
    let page = load(storage);
    assert.equal(page.theme.getDarkMode(), false);
    page.theme.setDarkMode(true);
    assert.equal(page.document.documentElement.dataset.theme, 'dark');
    page = load(storage);
    assert.equal(page.theme.getDarkMode(), true);
    page.theme.setDarkMode(false);
    assert.equal(load(storage).theme.getDarkMode(), false);
});
test('blocked browser storage still allows changing the current page', () => {
    const page = load({ getItem() { throw Error('blocked'); }, setItem() { throw Error('blocked'); } });
    page.theme.setDarkMode(true);
    assert.equal(page.theme.getDarkMode(), true);
});
test('theme follows changes in another tab and storage clearing', () => {
    const page = load({ getItem: () => 'light' });
    page.events.storage({ key: 'trucker-reward-theme', newValue: 'dark' });
    assert.equal(page.theme.getDarkMode(), true);
    page.events.storage({ key: null, newValue: null });
    assert.equal(page.theme.getDarkMode(), false);
});
