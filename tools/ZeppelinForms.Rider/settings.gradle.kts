// The Rider plugin: its own Gradle build, outside the .NET solution. The sandbox's and the
// zip's folder of the plugin is named after this project.
rootProject.name = "zeppelinforms-rider"

pluginManagement {
    repositories {
        gradlePluginPortal()
        mavenCentral()
    }
}
