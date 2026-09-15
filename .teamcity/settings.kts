import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildFeatures.commitStatusPublisher
import jetbrains.buildServer.configs.kotlin.buildSteps.*
import jetbrains.buildServer.configs.kotlin.triggers.vcs

/*
 * Generated 2026-09-09 as part of the Cloud migration (Platform onboarding).
 * Restore resolves through the agent's machine config (GitHub Packages + nuget.org).
 * Tests read shared test data straight from testroot_base via TESTDATA_INPUT_ROOT,
 * exactly as on-prem did - nothing is copied locally.
 *
 * Publish is a separate configuration with a snapshot dependency on Build, so a red
 * test run never publishes. The version is the top entry of ChangeLog.txt - the bump
 * is the release signal (see VM.IaC docs/teamcity.md, the Platform flow section).
 * Publishing is idempotent: .teamcity/publish.ps1 skips versions already on the feed.
 */

version = "2026.1"

project {

    params {
        // Same test data model as on-prem: read directly from the share.
        param("env.TESTDATA_INPUT_ROOT", """%testroot_base%\NuGet_Packages\VM.Lab.Interfaces.Session""")
        // IPP/MKL native libraries for the packages that load them at test time.
        param("env.Path", """%env.Path%;%testroot_base%\NuGet_Packages\IPP2021.6.2.19751\bin\intel64;%testroot_base%\NuGet_Packages\MKL2021Update1\x64""")
    }

    val build = buildType {
        id("Build")
        name = "Build"

        vcs {
            root(DslContext.settingsRoot)
        }

        steps {
            dotnetRestore {
                name = "Restore"
                projects = "src/VM.Lab.Interfaces.Session.sln"
            }
            dotnetBuild {
                name = "Build"
                projects = "src/VM.Lab.Interfaces.Session.sln"
                configuration = "Release"
            }
            dotnetTest {
                filter = "%vm.test.filter%"
                name = "Test"
                projects = "src/VM.Lab.Interfaces.Session.sln"
                configuration = "Release"
            }
        }

        features {
            commitStatusPublisher {
                publisher = github {
                    githubUrl = "https://api.github.com"
                    authType = vcsRoot()
                }
            }
        }

        triggers {
            vcs {
            }
        }

        requirements {
            equals("vmlab.role.build", "true")
        }
    }

    buildType {
        id("Publish")
        name = "Publish"

        vcs {
            root(DslContext.settingsRoot)
        }

        params {
            param("env.VM_FEED_TOKEN", "%vm.feed.github.token%")
            param("env.VM_PUBLISHING_DRIVE", "%publishing_drive%")
        }

        steps {
            powerShell {
                name = "Publish VM.Lab.Interfaces.Session"
                id = "PUBLISH_VMLabInterfacesSession"
                edition = PowerShellStep.Edition.Desktop
                formatStderrAsError = true
                scriptMode = file { path = ".teamcity/publish.ps1" }
                scriptArgs = "-PackageId VM.Lab.Interfaces.Session -Nuspec src/VM.Lab.Interfaces.Session.nuspec -Sln src/VM.Lab.Interfaces.Session.sln"
            }
        }

        triggers {
            vcs {
                // A PR must never publish: package versions are immutable, so a
                // pre-merge publish burns the version with unreviewed content.
                // PRs run Build only; Publish fires on real branches at merge.
                branchFilter = """
                    +:*
                    -:*/merge
                """.trimIndent()
            }
        }

        dependencies {
            snapshot(build) {
                onDependencyFailure = FailureAction.FAIL_TO_START
                onDependencyCancel = FailureAction.CANCEL
            }
        }

        requirements {
            equals("vmlab.role.build", "true")
        }
    }
}

