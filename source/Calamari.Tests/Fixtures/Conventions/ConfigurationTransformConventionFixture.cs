﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calamari.Deployment;
using Calamari.Deployment.Conventions;
using Calamari.Integration.ConfigurationTransforms;
using Calamari.Integration.FileSystem;
using NSubstitute;
using NUnit.Framework;
using Octostache;

namespace Calamari.Tests.Fixtures.Conventions
{
    [TestFixture]
    public class ConfigurationTransformConventionFixture
    {
        ICalamariFileSystem fileSystem;
        IConfigurationTransformer configurationTransformer;
        RunningDeployment deployment;
        VariableDictionary variables;
        const string stagingDirectory = "c:\\applications\\acme\\1.0.0";

        [SetUp]
        public void SetUp()
        {
            fileSystem = Substitute.For<ICalamariFileSystem>();
            configurationTransformer = Substitute.For<IConfigurationTransformer>();

            variables = new VariableDictionary();
            variables.Set(SpecialVariables.OriginalPackageDirectoryPath, stagingDirectory);

            deployment = new RunningDeployment("C:\\packages", variables);
        }

        [Test]
        public void ShouldApplyReleaseTransformIfAutomaticallyRunConfigurationTransformationFilesFlagIsSet()
        {
            var webConfig = Path.Combine(stagingDirectory, "web.config");
            var webConfigReleaseTransform = Path.Combine(stagingDirectory, "web.Release.config");

            MockSearchableFiles(fileSystem, stagingDirectory, new[] { webConfig, webConfigReleaseTransform }, "*.config");

            variables.Set(SpecialVariables.Package.AutomaticallyRunConfigurationTransformationFiles, true.ToString());
            CreateConvention().Install(deployment);

            configurationTransformer.Received().PerformTransform(webConfig, webConfigReleaseTransform, webConfig);
        }

        [Test]
        public void ShouldNotApplyReleaseTransformIfAutomaticallyRunConfigurationTransformationFilesFlagNotSet()
        {
            var webConfig = Path.Combine(stagingDirectory, "web.config");
            var webConfigReleaseTransform = Path.Combine(stagingDirectory, "web.Release.config");

            MockSearchableFiles(fileSystem, stagingDirectory, new[] { webConfig, webConfigReleaseTransform }, "*.config");

            variables.Set(SpecialVariables.Package.AutomaticallyRunConfigurationTransformationFiles, false.ToString());
            CreateConvention().Install(deployment);

            configurationTransformer.DidNotReceive().PerformTransform(webConfig, webConfigReleaseTransform, webConfig);
        }

        [Test]
        public void ShouldApplyEnvironmentTransform()
        {
            const string environment = "Production";
            var webConfig = Path.Combine(stagingDirectory, "web.config");
            var environmentTransform = Path.Combine(stagingDirectory, "web.Production.config");

            MockSearchableFiles(fileSystem, stagingDirectory, new[] { webConfig, environmentTransform }, "*.config");

            variables.Set(SpecialVariables.Package.AutomaticallyRunConfigurationTransformationFiles, true.ToString());
            variables.Set(SpecialVariables.Environment.Name, environment);
            CreateConvention().Install(deployment);

            configurationTransformer.Received().PerformTransform(webConfig, environmentTransform, webConfig);
        }

        [Test]
        public void ShouldApplySpecificCustomTransform()
        {
            var webConfig = Path.Combine(stagingDirectory, "web.config");
            var specificTransform = Path.Combine(stagingDirectory, "web.Foo.config");

            MockSearchableFiles(fileSystem, stagingDirectory, new[] { webConfig, specificTransform }, "*.config");

            variables.Set(SpecialVariables.Package.AdditionalXmlConfigurationTransforms, "web.Foo.config => web.config");
            // This will be applied even if the automatically run flag is set to false
            variables.Set(SpecialVariables.Package.AutomaticallyRunConfigurationTransformationFiles, false.ToString());
            CreateConvention().Install(deployment);

            configurationTransformer.Received().PerformTransform(webConfig, specificTransform, webConfig);
        }

        private static IEnumerable WildcardTransformTestCases
        {
            get
            {
                yield return new TestCaseData("*.Foo.config=>*.Bar.config");
                yield return new TestCaseData("*.Foo.config=>Bar.config");
                yield return new TestCaseData("Foo.config=>*.Bar.config");
            }
        }

        [Test, TestCaseSource("WildcardTransformTestCases")]
        public void RunsAdvancedWildcardConfigTransform(string transformDefinition)
        {
            var sourceConfig = Path.Combine(stagingDirectory, "xyz.Bar.config");
            var transform = Path.Combine(stagingDirectory, "xyz.Foo.config");

            MockSearchableFiles(fileSystem, stagingDirectory, new[] { sourceConfig, transform }, "*.config");

            variables.Set(SpecialVariables.Package.AdditionalXmlConfigurationTransforms, transformDefinition);
            // This will be applied even if the automatically run flag is set to false
            variables.Set(SpecialVariables.Package.AutomaticallyRunConfigurationTransformationFiles, false.ToString());
            CreateConvention().Install(deployment);

            configurationTransformer.Received().PerformTransform(sourceConfig, transform, sourceConfig);
        }

        private static IEnumerable<Tuple<string, string, string>> TransformFileNameTestCases
        {
            get
            {
                yield return Tuple.Create("C:\\Some\\path\\to\\web.config", "Release", "C:\\Some\\path\\to\\web.Release.config");
                yield return Tuple.Create("C:\\Some\\path\\to\\web.config", "Staging.QLD", "C:\\Some\\path\\to\\web.Staging.QLD.config");
                yield return Tuple.Create("C:\\Some\\path\\to\\web.config", "Production.config", "C:\\Some\\path\\to\\web.Production.config");
                yield return Tuple.Create("C:\\Some\\path\\to\\bar.config", "foo.config=>bar.config", "C:\\Some\\path\\to\\foo.config");
                yield return Tuple.Create("C:\\Some\\path\\to\\bar.blah", "foo.baz=>bar.blah", "C:\\Some\\path\\to\\foo.baz");
                yield return Tuple.Create("C:\\Some\\path\\to\\bar.config", "foo.xml=>bar.config", "C:\\Some\\path\\to\\foo.xml");
                yield return Tuple.Create("C:\\Some\\path\\to\\xyz.bar.blah", "*.foo.blah=>*.bar.blah", "C:\\Some\\path\\to\\xyz.foo.blah");
                yield return Tuple.Create("C:\\Some\\path\\to\\xyz.bar.blah", "foo.blah=>*.bar.blah", "C:\\Some\\path\\to\\xyz.foo.blah");
                yield return Tuple.Create("C:\\Some\\path\\to\\xyz.bar.blah", "*.foo.blah=>bar.blah", "C:\\Some\\path\\to\\xyz.foo.blah");
                yield return Tuple.Create("C:\\Some\\path\\to\\crossdomainpolicy.xml", "Production.xml", "C:\\Some\\path\\to\\crossdomainpolicy.Production.xml");
            }
        }

        [Test]
        public void DetermineCorrectTransformFileName()
        {
            foreach (var set in TransformFileNameTestCases)
            {
                string sourceFile = set.Item1, transformDefinition = set.Item2, required = set.Item3;

                var transform = new XmlConfigTransformDefinition(transformDefinition);
                var all = ConfigurationTransformsConvention.DetermineTransformFileNames(sourceFile, transform);
                var matched = all.Contains(required);
                Assert.That(matched);
            }
        }

        private ConfigurationTransformsConvention CreateConvention()
        {
            return new ConfigurationTransformsConvention(fileSystem, configurationTransformer);
        }

        private static void MockSearchableFiles(ICalamariFileSystem fileSystem, string parentDirectory, string[] files, string searchPattern)
        {
            fileSystem.EnumerateFilesRecursively(parentDirectory,
                Arg.Is<string[]>(x => new List<string>(x).Contains(searchPattern))).Returns(files);

            foreach (var file in files)
                fileSystem.FileExists(file).Returns(true);
        }
    }
}