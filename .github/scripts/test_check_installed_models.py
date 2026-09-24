import unittest

from check_installed_models import problems


class InstalledModelsTest(unittest.TestCase):
    def setUp(self):
        self.expected = [{'name': 'Sales', 'modelVersion': '1.2.0'}]
        self.installed = [{'name': 'Sales', 'version': '1.2.0',
                           'packageVersion': '1.2.0', 'enabled': True, 'status': 'Ok'}]

    def test_matching_install(self):
        self.assertEqual([], problems(self.expected, self.installed))

    def test_package_stamp_cannot_hide_stale_model(self):
        self.installed[0]['version'] = '1.1.0'
        self.assertIn('model=1.1.0', problems(self.expected, self.installed)[0])

    def test_model_cannot_hide_stale_or_missing_package(self):
        for version in ('1.1.0', None):
            with self.subTest(version=version):
                self.installed[0]['packageVersion'] = version
                self.assertTrue(problems(self.expected, self.installed))

    def test_missing_model(self):
        self.assertIn('missing', problems(self.expected, [])[0])

    def test_disabled_or_failed_model(self):
        for enabled, status in ((False, 'Ok'), (True, 'Failed'),
                                (True, 'DependencyFailed'), (True, None)):
            with self.subTest(enabled=enabled, status=status):
                self.installed[0].update(enabled=enabled, status=status)
                self.assertTrue(problems(self.expected, self.installed))

    def test_core_uses_platform_version_but_must_compile(self):
        self.expected[0].update(name='Core', isSystem=True, modelVersion='0.0.0')
        self.installed[0].update(name='Core', version='2026.0.270',
                                 packageVersion='2026.0.270')
        self.assertEqual([], problems(self.expected, self.installed))
        self.installed[0]['status'] = 'Failed'
        self.assertTrue(problems(self.expected, self.installed))


if __name__ == '__main__':
    unittest.main()
