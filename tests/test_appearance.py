"""外观 is one team setting: a theme and one of its palettes, Anthropic风 原色
until an admin picks another, and part of everyone's state so each page can
follow it."""
import tempfile
import unittest
from pathlib import Path

from server import Store, Problem, THEMES


class AppearanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / 'appearance.sqlite3', seed=False)
        for name in ('superadmin', 'test001'):
            self.store.create_user(name, 'password-long', name == 'superadmin')
        self.admin, self.member = self.store.user('superadmin'), self.store.user('test001')

    def tearDown(self):
        self.store.close()
        self.tmp.cleanup()

    def test_defaults_to_the_first_theme_and_palette(self):
        self.assertEqual(self.store.appearance(), {'theme': 'anthropic', 'palette': 'default'})
        self.assertEqual(self.store.snapshot(self.member)['appearance'], {'theme': 'anthropic', 'palette': 'default'})

    def test_an_admin_sets_it_and_every_member_receives_it(self):
        result = self.store.action(self.admin, 'appearance.update', {'theme': 'tempo', 'palette': 'phosphor'})
        self.assertEqual(result, {'theme': 'tempo', 'palette': 'phosphor'})
        self.assertEqual(self.store.snapshot(self.member)['appearance'], {'theme': 'tempo', 'palette': 'phosphor'})
        self.assertEqual(self.store.snapshot(self.admin)['appearance'], {'theme': 'tempo', 'palette': 'phosphor'})
        row = next(x for x in self.store.all('settings') if x['id'] == 'appearance')
        self.assertEqual(row['value'], {'theme': 'tempo', 'palette': 'phosphor'})
        self.assertEqual(row['by'], 'superadmin')

    def test_members_cannot_set_it(self):
        with self.assertRaises(Problem) as caught:
            self.store.action(self.member, 'appearance.update', {'theme': 'tempo', 'palette': 'paper'})
        self.assertEqual(caught.exception.status, 403)
        self.assertEqual(self.store.appearance(), {'theme': 'anthropic', 'palette': 'default'})

    def test_only_known_themes_and_their_own_palettes_are_accepted(self):
        for data in ({'theme': 'neon', 'palette': 'default'}, {'theme': 'anthropic', 'palette': 'phosphor'},
                     {'theme': 'tempo', 'palette': 'dusk'}, {'theme': 'anthropic'}, {}, {'theme': None, 'palette': None},
                     {'theme': ['anthropic'], 'palette': 'default'}):
            with self.assertRaises(Problem, msg=data) as caught:
                self.store.action(self.admin, 'appearance.update', data)
            self.assertEqual(caught.exception.status, 400)
        self.assertEqual(self.store.appearance(), {'theme': 'anthropic', 'palette': 'default'})
        for theme, palettes in THEMES.items():
            for palette in palettes:
                self.assertEqual(self.store.action(self.admin, 'appearance.update', {'theme': theme, 'palette': palette}),
                                 {'theme': theme, 'palette': palette})

    def test_a_stored_value_the_page_no_longer_knows_falls_back(self):
        self.store.put('settings', {'id': 'appearance', 'value': {'theme': 'tempo', 'palette': 'retired'}})
        self.assertEqual(self.store.appearance(), {'theme': 'tempo', 'palette': 'paper'}, 'the theme keeps its first palette')
        self.store.put('settings', {'id': 'appearance', 'value': {'theme': 'retired', 'palette': 'paper'}})
        self.assertEqual(self.store.appearance(), {'theme': 'anthropic', 'palette': 'default'})
        self.store.put('settings', {'id': 'appearance', 'value': 'tempo'})
        self.assertEqual(self.store.appearance(), {'theme': 'anthropic', 'palette': 'default'})

    def test_the_stylesheet_and_page_know_every_palette(self):
        root = Path(__file__).resolve().parent.parent
        css = (root / 'styles.css').read_text(encoding='utf-8')
        app = (root / 'app.js').read_text(encoding='utf-8')
        for theme, palettes in THEMES.items():
            for palette in palettes:
                if palette != palettes[0]:  # a theme's first palette is the theme block itself
                    self.assertIn(f'[data-theme="{theme}"][data-palette="{palette}"]', css)
                self.assertRegex(app, rf'\b{palette}:\{{name:', f'app.js lists {theme}/{palette}')
        index = (root / 'index.html').read_text(encoding='utf-8')
        self.assertLess(index.index('hola-appearance'), index.index('rel="stylesheet"'), 'the theme is set before the stylesheet loads')


if __name__ == '__main__':
    unittest.main()
