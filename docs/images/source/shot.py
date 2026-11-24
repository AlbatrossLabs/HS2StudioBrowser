import sys, pathlib
from playwright.sync_api import sync_playwright
here = pathlib.Path(__file__).parent.resolve()
sizes = {'banner': (1600, 400), 'social': (1280, 640), 'social32': (1200, 800)}
with sync_playwright() as p:
    b = p.chromium.launch(executable_path='/usr/bin/google-chrome') if pathlib.Path('/usr/bin/google-chrome').exists() else p.chromium.launch()
    for name, (w, h) in sizes.items():
        pg = b.new_page(viewport={'width': w, 'height': h}, device_scale_factor=2)
        pg.goto(f'file://{here}/banner.html?layout={name}')
        pg.wait_for_timeout(400)
        pg.locator('.stage').screenshot(path=str(here.parent / {'banner': 'banner.png', 'social': 'social-preview.png', 'social32': 'social-post-3x2.png'}[name]))  # social: resize to 1280 x 640 after, GitHub takes up to 1 MB
    b.close()
