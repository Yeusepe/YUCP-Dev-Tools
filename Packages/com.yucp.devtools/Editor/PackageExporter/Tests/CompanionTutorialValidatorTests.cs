using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Tests
{
    public class CompanionTutorialValidatorTests
    {
        private static CompanionTutorialDefinition WithStep(CompanionTutorialStep step)
        {
            return new CompanionTutorialDefinition
            {
                enabled = true,
                title = "T",
                steps = new List<CompanionTutorialStep> { step }
            };
        }

        private static CompanionTutorialStep ValidStep()
        {
            return new CompanionTutorialStep
            {
                title = "Title",
                text = "Body",
                target = "inspector",
                waitFor = "manual",
                mouseAction = "none",
                overlayMode = "intrusive"
            };
        }

        private static bool Has(List<CompanionTutorialValidator.Finding> findings, CompanionTutorialValidator.Severity sev)
        {
            return findings.Any(f => f.Severity == sev);
        }

        [Test]
        public void DisabledTutorial_ProducesNoFindings()
        {
            var def = WithStep(ValidStep());
            def.enabled = false;
            Assert.That(CompanionTutorialValidator.Validate(def), Is.Empty);
        }

        [Test]
        public void EnabledWithNoSteps_Warns()
        {
            var def = new CompanionTutorialDefinition { enabled = true, steps = new List<CompanionTutorialStep>() };
            var findings = CompanionTutorialValidator.Validate(def);
            Assert.That(Has(findings, CompanionTutorialValidator.Severity.Warning), Is.True);
        }

        [Test]
        public void ValidStep_HasNoErrorsOrWarnings()
        {
            var findings = CompanionTutorialValidator.Validate(WithStep(ValidStep()));
            Assert.That(Has(findings, CompanionTutorialValidator.Severity.Error), Is.False);
            Assert.That(Has(findings, CompanionTutorialValidator.Severity.Warning), Is.False);
        }

        [Test]
        public void DelayWithoutNumber_IsError()
        {
            var step = ValidStep();
            step.waitFor = "delay:";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Error), Is.True);
        }

        [Test]
        public void DelayWithNumber_IsValid()
        {
            var step = ValidStep();
            step.waitFor = "delay:2.5";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Error), Is.False);
        }

        [Test]
        public void AssetExistsWithEmptyArg_IsError()
        {
            var step = ValidStep();
            step.waitFor = "assetExists:";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Error), Is.True);
        }

        [Test]
        public void EmptySelectorAfterKnownPrefix_IsError()
        {
            var step = ValidStep();
            step.target = "toolbar:";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Error), Is.True);
        }

        [Test]
        public void UnknownTargetPrefix_Warns()
        {
            var step = ValidStep();
            step.target = "bogus:thing";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Warning), Is.True);
        }

        [Test]
        public void UnknownBareTarget_Warns()
        {
            var step = ValidStep();
            step.target = "nonsense";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Warning), Is.True);
        }

        [Test]
        public void EmptyTitleAndText_Warn()
        {
            var step = ValidStep();
            step.title = "";
            step.text = "";
            var findings = CompanionTutorialValidator.Validate(WithStep(step));
            Assert.That(findings.Count(f => f.Severity == CompanionTutorialValidator.Severity.Warning), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void UnknownMouseAction_Warns()
        {
            var step = ValidStep();
            step.mouseAction = "wiggle";
            Assert.That(Has(CompanionTutorialValidator.Validate(WithStep(step)), CompanionTutorialValidator.Severity.Warning), Is.True);
        }

        [Test]
        public void ManualRectOverride_MakesTargetOptional()
        {
            var step = ValidStep();
            step.target = ""; // would normally be Info, but a full rect override should suppress target issues
            step.targetRect = new Vector4(0, 0, 100, 50);
            var findings = CompanionTutorialValidator.Validate(WithStep(step));
            Assert.That(Has(findings, CompanionTutorialValidator.Severity.Error), Is.False);
        }
    }
}
