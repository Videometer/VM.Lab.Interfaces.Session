import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildSteps.*
import jetbrains.buildServer.configs.kotlin.triggers.vcs

/*
 * Generated 2026-09-08 as part of the Cloud migration (Platform onboarding).
 * Build and test only. Publish is added later, together with the versioning
 * decision and the feed write token - see VM.IaC docs/teamcity.md, Platform-flowet.
 * Restore resolves through the agent's machine config (GitHub Packages + nuget.org).
 */

version = "2026.1"

project {

    buildType {
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
                name = "Test"
                projects = "src/VM.Lab.Interfaces.Session.sln"
                configuration = "Release"
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
}
