import org.jetbrains.intellij.platform.gradle.TestFrameworkType
import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.dsl.KotlinVersion

// The Rider plugin: a frontend plugin in Kotlin, without a ReSharper part. Completion and
// problems come from the ZeppelinForms language server, the preview from the previewer's
// host — both .NET programs of this repository, published by the build into the plugin's
// server/ and designer/ folders:
//
//   ./gradlew buildPlugin                     build/distributions/zeppelinforms-rider-<version>.zip
//   ./gradlew buildPlugin -PzfVersion=0.19.0  the .NET programs of that version (CI: the tag's)
//   ./gradlew runIde                          a Rider with the plugin
//   ./gradlew test                            the protocol's codec, the key codes, the project's output
//                                             (and a conversation with a host: ZF_DESIGNER_HOST, ZF_PREVIEW_ASSEMBLY)

plugins {
    id("org.jetbrains.kotlin.jvm") version "2.4.21"
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

group = "com.yd359team.zeppelinforms"
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()

    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // the multi-OS archive from the IntelliJ Maven repository, as JetBrains' Rider plugin template does
        rider(providers.gradleProperty("riderVersion")) {
            useInstaller = false
        }

        // the LSP API is a module of the platform since it is open source (2026.1)
        bundledModule("intellij.platform.lsp")
        bundledPlugin("org.jetbrains.plugins.textmate")

        testFramework(TestFrameworkType.Platform)
    }

    testImplementation(platform("org.junit:junit-bom:5.13.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")

    // the platform's test framework is JUnit 4 inside
    testImplementation("junit:junit:4.13.2")
}

kotlin {
    jvmToolchain(21)

    compilerOptions {
        jvmTarget = JvmTarget.JVM_21

        // the IDE's own Kotlin library runs the plugin: nothing newer than Rider 2025.2 has
        apiVersion = KotlinVersion.KOTLIN_2_1
    }
}

intellijPlatform {
    pluginConfiguration {
        version = providers.gradleProperty("pluginVersion")

        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = provider { null }
        }
    }

    // nothing of the plugin is in Settings
    buildSearchableOptions = false

    // ./gradlew publishPlugin: the release workflow, with the JetBrains Marketplace token
    publishing {
        token = providers.environmentVariable("JETBRAINS_MARKETPLACE_TOKEN")
    }
}

// ===== the .NET programs =====

val repository = layout.projectDirectory.dir("../..")
val dotnetOutput = layout.buildDirectory.dir("dotnet")

/** The host must be of the framework's version: a host of another one can't bind a project built against it. */
val versionArgument = providers.gradleProperty("zfVersion").map { listOf("-p:Version=$it") }.orElse(emptyList())

val publishLanguageServer by tasks.registering(Exec::class) {
    group = "zeppelinforms"
    description = "Publishes the ZeppelinForms language server into build/dotnet/server."

    val csproj = repository.file("tools/ZeppelinForms.LanguageServer/ZeppelinForms.LanguageServer.csproj").asFile
    val output = dotnetOutput.get().dir("server").asFile

    outputs.dir(output)
    commandLine(listOf("dotnet", "publish", csproj.path, "-c", "Release", "-o", output.path, "-nologo", "-v", "minimal") + versionArgument.get())
}

val publishDesigner by tasks.registering(Exec::class) {
    group = "zeppelinforms"
    description = "Publishes the previewer's host into build/dotnet/designer."

    val csproj = repository.file("tools/ZeppelinForms.Designer/ZeppelinForms.Designer.csproj").asFile
    val output = dotnetOutput.get().dir("designer").asFile

    outputs.dir(output)
    commandLine(listOf("dotnet", "publish", csproj.path, "-c", "Release", "-o", output.path, "-nologo", "-v", "minimal") + versionArgument.get())
}

tasks {
    prepareSandbox {
        dependsOn(publishLanguageServer, publishDesigner)

        val plugin = rootProject.name
        val grammars = repository.dir("tools/ZeppelinForms.VisualStudio/Grammars")

        from(dotnetOutput.map { it.dir("server") }) {
            exclude("*.pdb")
            into("$plugin/server")
        }

        from(dotnetOutput.map { it.dir("designer") }) {
            exclude("*.pdb")
            into("$plugin/designer")
        }

        // .zss's grammar: Visual Studio's, in a bundle of VS Code's shape; without the byte
        // order mark the Visual Studio files have
        from(layout.projectDirectory.dir("textmate")) {
            into("$plugin/textmate")
        }

        from(grammars.file("zss.tmLanguage.json")) {
            filter { line: String -> line.removePrefix("﻿") }
            into("$plugin/textmate/zss/syntaxes")
        }

        from(grammars.file("language-configuration.json")) {
            filter { line: String -> line.removePrefix("﻿") }
            into("$plugin/textmate/zss")
        }
    }

    test {
        useJUnitPlatform()

        // a whole conversation with a host, when one is given: ZF_DESIGNER_HOST and ZF_PREVIEW_ASSEMBLY
        environment("ZF_DOTNET", System.getenv("ZF_DOTNET") ?: "dotnet")
    }
}
