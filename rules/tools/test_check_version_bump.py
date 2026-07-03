#!/usr/bin/env python3
"""
Unit and integration tests for check_version_bump.py.
Tests the version bump verification script against git diffs and Directory.Build.props.
"""

import os
import sys
import unittest
from unittest.mock import patch, MagicMock, mock_open

import pytest

# Add current tools directory to sys.path to resolve imports correctly
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
if TOOLS_DIR not in sys.path:
    sys.path.insert(0, TOOLS_DIR)

# Attempt to import check_version_bump from rules/tools.
# Since it doesn't exist yet, we catch the ImportError.
# This makes tests fail dynamically with a helpful message, allowing collection to succeed.
try:
    import check_version_bump as cvb
except ImportError:
    try:
        import rules.tools.check_version_bump as cvb
    except ImportError:
        cvb = None


class TestCheckVersionBumpImport(unittest.TestCase):
    def test_module_exists(self):
        """Verify that check_version_bump module is implemented and imported."""
        if cvb is None:
            self.fail(
                "Module 'check_version_bump' could not be imported. "
                "Please implement rules/tools/check_version_bump.py with the expected logic."
            )


class TestVersionComparison(unittest.TestCase):
    def test_semver_greater(self):
        """Test SemVer strictly greater-than comparison logic."""
        if cvb is None:
            self.skipTest("check_version_bump not implemented yet")

        # Supposing the script exposes an 'is_semver_greater' helper
        if hasattr(cvb, "is_semver_greater"):
            self.assertTrue(cvb.is_semver_greater("0.1.1", "0.1.0"))
            self.assertTrue(cvb.is_semver_greater("0.2.0", "0.1.0"))
            self.assertTrue(cvb.is_semver_greater("1.0.0", "0.1.0"))
            self.assertTrue(cvb.is_semver_greater("1.10.2", "1.9.5"))
            self.assertTrue(cvb.is_semver_greater("2.0.0-alpha.2", "1.9.9"))

            self.assertFalse(cvb.is_semver_greater("0.1.0", "0.1.0"))
            self.assertFalse(cvb.is_semver_greater("0.0.9", "0.1.0"))
            self.assertFalse(cvb.is_semver_greater("0.1.0-alpha", "0.1.0"))
            self.assertFalse(cvb.is_semver_greater("1.0.0", "1.0.1"))
        else:
            self.fail("check_version_bump lacks 'is_semver_greater' helper function")


class TestGameplayAffectingClassification(unittest.TestCase):
    def test_is_gameplay_affecting(self):
        """Test classification of various file paths."""
        if cvb is None:
            self.skipTest("check_version_bump not implemented yet")

        if not hasattr(cvb, "is_gameplay_affecting"):
            self.fail("check_version_bump lacks 'is_gameplay_affecting' helper function")

        # Engine/Rules bucket: src/Pinder.Core/**, src/Pinder.LlmAdapters/**
        self.assertTrue(cvb.is_gameplay_affecting("src/Pinder.Core/Game.cs"))
        self.assertTrue(cvb.is_gameplay_affecting("src/Pinder.LlmAdapters/OpenAi/OpenAiTransport.cs"))

        # Schema/Data bucket: data/anatomy/*.json, data/items/*.json, data/characters/character-schema.json
        self.assertTrue(cvb.is_gameplay_affecting("data/anatomy/head.json"))
        self.assertTrue(cvb.is_gameplay_affecting("data/items/weapon.json"))
        self.assertTrue(cvb.is_gameplay_affecting("data/characters/character-schema.json"))

        # Prompts bucket: Prompts/**, prompts/*.yaml
        self.assertTrue(cvb.is_gameplay_affecting("Prompts/system_prompt.txt"))
        self.assertTrue(cvb.is_gameplay_affecting("prompts/dialogue.yaml"))
        self.assertTrue(cvb.is_gameplay_affecting("data/prompts/templates.yaml"))

        # Non-gameplay affecting / test-only / doc-only / config / helper files
        self.assertFalse(cvb.is_gameplay_affecting("tests/Pinder.Core.Tests/GameTests.cs"))
        self.assertFalse(cvb.is_gameplay_affecting("docs/rules-dsl.md"))
        self.assertFalse(cvb.is_gameplay_affecting("README.md"))
        self.assertFalse(cvb.is_gameplay_affecting(".gitignore"))
        self.assertFalse(cvb.is_gameplay_affecting("rules/tools/test_check_version_bump.py"))
        self.assertFalse(cvb.is_gameplay_affecting("data/anatomy/readme.txt"))  # Not a JSON
        self.assertFalse(cvb.is_gameplay_affecting("data/characters/character-backstory.json"))  # Schema only is character-schema.json


class TestCheckVersionBumpCLI(unittest.TestCase):
    """
    Integration-level tests that simulate running check_version_bump.py via its main/CLI interface.
    These mock the git operations and local file access to verify correct exit status codes.
    """

    def setUp(self):
        if cvb is None:
            self.skipTest("check_version_bump not implemented yet")

    def _create_mock_completed_process(self, stdout="", returncode=0):
        mock_res = MagicMock()
        mock_res.stdout = stdout
        mock_res.returncode = returncode
        return mock_res

    @patch("sys.exit")
    @patch("builtins.open", new_callable=mock_open)
    @patch("subprocess.run")
    def test_case_1_gameplay_changed_and_version_bumped_passes(self, mock_subprocess_run, mock_file_open, mock_sys_exit):
        """
        Case 1: Engine/schema/prompt files changed, version strictly bumped -> passes.
        """
        # 1. Mock git diff output to show gameplay-affecting files changed
        diff_output = "src/Pinder.Core/Engine.cs\ndata/anatomy/torso.json\nprompts/narrative.yaml\n"
        
        # 2. Mock origin/main version in Directory.Build.props (old version = 0.1.0)
        old_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"
        
        # 3. Mock local version in Directory.Build.props (new version = 0.2.0)
        new_props_content = "<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>"

        # Set up subprocess mocks
        def subprocess_side_effect(cmd, *args, **kwargs):
            cmd_str = " ".join(cmd) if isinstance(cmd, list) else cmd
            if "diff" in cmd_str:
                return self._create_mock_completed_process(stdout=diff_output)
            elif "show" in cmd_str and "origin/main" in cmd_str:
                return self._create_mock_completed_process(stdout=old_props_content)
            return self._create_mock_completed_process()

        mock_subprocess_run.side_effect = subprocess_side_effect
        mock_file_open.return_value.read.return_value = new_props_content

        # Run check_version_bump script entrypoint (e.g., cvb.main())
        if hasattr(cvb, "main"):
            cvb.main()
            # Ensure it exited with 0 (success)
            mock_sys_exit.assert_called_once_with(0)
        else:
            self.fail("check_version_bump lacks 'main' function")

    @patch("sys.exit")
    @patch("builtins.open", new_callable=mock_open)
    @patch("subprocess.run")
    def test_case_2_gameplay_changed_and_version_not_bumped_fails(self, mock_subprocess_run, mock_file_open, mock_sys_exit):
        """
        Case 2: Engine/schema/prompt files changed, version NOT bumped -> fails.
        """
        # 1. Mock git diff output to show gameplay-affecting files changed
        diff_output = "src/Pinder.Core/Engine.cs\n"
        
        # 2. Mock origin/main version in Directory.Build.props (old version = 0.1.0)
        old_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"
        
        # 3. Mock local version in Directory.Build.props (new version = 0.1.0 - same)
        new_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"

        # Set up subprocess mocks
        def subprocess_side_effect(cmd, *args, **kwargs):
            cmd_str = " ".join(cmd) if isinstance(cmd, list) else cmd
            if "diff" in cmd_str:
                return self._create_mock_completed_process(stdout=diff_output)
            elif "show" in cmd_str and "origin/main" in cmd_str:
                return self._create_mock_completed_process(stdout=old_props_content)
            return self._create_mock_completed_process()

        mock_subprocess_run.side_effect = subprocess_side_effect
        mock_file_open.return_value.read.return_value = new_props_content

        if hasattr(cvb, "main"):
            cvb.main()
            # Ensure it exited with a non-zero code (failure)
            self.assertTrue(mock_sys_exit.called)
            exit_code = mock_sys_exit.call_args[0][0]
            self.assertNotEqual(exit_code, 0)
        else:
            self.fail("check_version_bump lacks 'main' function")

    @patch("sys.exit")
    @patch("builtins.open", new_callable=mock_open)
    @patch("subprocess.run")
    def test_case_3_gameplay_changed_and_version_downgraded_fails(self, mock_subprocess_run, mock_file_open, mock_sys_exit):
        """
        Case 3: Engine/schema/prompt files changed, version downgraded or same-value "bump" -> fails.
        """
        # 1. Mock git diff output to show gameplay-affecting files changed
        diff_output = "src/Pinder.LlmAdapters/Connection.cs\n"
        
        # 2. Mock origin/main version in Directory.Build.props (old version = 0.2.0)
        old_props_content = "<Project><PropertyGroup><Version>0.2.0</Version></PropertyGroup></Project>"
        
        # 3. Mock local version in Directory.Build.props (new version = 0.1.0 - downgraded)
        new_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"

        # Set up subprocess mocks
        def subprocess_side_effect(cmd, *args, **kwargs):
            cmd_str = " ".join(cmd) if isinstance(cmd, list) else cmd
            if "diff" in cmd_str:
                return self._create_mock_completed_process(stdout=diff_output)
            elif "show" in cmd_str and "origin/main" in cmd_str:
                return self._create_mock_completed_process(stdout=old_props_content)
            return self._create_mock_completed_process()

        mock_subprocess_run.side_effect = subprocess_side_effect
        mock_file_open.return_value.read.return_value = new_props_content

        if hasattr(cvb, "main"):
            cvb.main()
            # Ensure it exited with a non-zero code (failure)
            self.assertTrue(mock_sys_exit.called)
            exit_code = mock_sys_exit.call_args[0][0]
            self.assertNotEqual(exit_code, 0)
        else:
            self.fail("check_version_bump lacks 'main' function")

    @patch("sys.exit")
    @patch("builtins.open", new_callable=mock_open)
    @patch("subprocess.run")
    def test_case_4_only_non_gameplay_changed_and_version_not_bumped_passes(self, mock_subprocess_run, mock_file_open, mock_sys_exit):
        """
        Case 4: Docs-only/test-only files changed, version NOT bumped -> passes.
        """
        # 1. Mock git diff output to show non-gameplay affecting changes only
        diff_output = "tests/Pinder.Core.Tests/SomeTest.cs\ndocs/architecture.md\nREADME.md\n"
        
        # 2. Mock origin/main version in Directory.Build.props (old version = 0.1.0)
        old_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"
        
        # 3. Mock local version in Directory.Build.props (new version = 0.1.0 - same)
        new_props_content = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>"

        # Set up subprocess mocks
        def subprocess_side_effect(cmd, *args, **kwargs):
            cmd_str = " ".join(cmd) if isinstance(cmd, list) else cmd
            if "diff" in cmd_str:
                return self._create_mock_completed_process(stdout=diff_output)
            elif "show" in cmd_str and "origin/main" in cmd_str:
                return self._create_mock_completed_process(stdout=old_props_content)
            return self._create_mock_completed_process()

        mock_subprocess_run.side_effect = subprocess_side_effect
        mock_file_open.return_value.read.return_value = new_props_content

        if hasattr(cvb, "main"):
            cvb.main()
            # Ensure it exited with 0 (success)
            mock_sys_exit.assert_called_once_with(0)
        else:
            self.fail("check_version_bump lacks 'main' function")


if __name__ == "__main__":
    unittest.main()
