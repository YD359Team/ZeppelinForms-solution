package com.yd359team.zeppelinforms.rider.designer

import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.extension
import kotlin.io.path.isDirectory
import kotlin.io.path.name
import kotlin.io.path.nameWithoutExtension

/**
 * Which project a file belongs to, and where the project's build puts its assembly: the
 * nearest .csproj above the file, its AssemblyName, and the newest assembly of that name
 * under its bin folder — the configuration and the framework last built. The same answer
 * the VS Code extension gives, from the files alone: Rider's project model is on its .NET
 * side, out of a frontend plugin's reach.
 */
internal object ProjectOutput {
    data class Output(val assemblyName: String, val binDirectory: Path, val assembly: Path?)

    /** The project file the file belongs to: the nearest *.csproj in its folder or above, up to [root]. */
    fun projectOf(file: Path, root: Path?): Path? {
        var directory: Path? = file.parent

        while (directory != null) {
            projectIn(directory)?.let { return it }

            if (root != null && (!directory.startsWith(root) || directory == root)) return null
            directory = directory.parent
        }

        return null
    }

    /** The folder's project file; of several, the one named as the folder. */
    private fun projectIn(directory: Path): Path? {
        val projects = try {
            Files.list(directory).use { entries -> entries.filter { it.extension.equals("csproj", ignoreCase = true) }.toList() }
        } catch (_: IOException) {
            return null
        }

        return projects.firstOrNull { it.nameWithoutExtension == directory.name } ?: projects.firstOrNull()
    }

    /** The project's AssemblyName, or the file's name; one built from properties — $(...) — can't be read here. */
    fun assemblyNameOf(project: Path): String {
        try {
            val text = Files.readString(project)
            val match = Regex("<AssemblyName>\\s*([^<]+?)\\s*</AssemblyName>").find(text)

            if (match != null && !match.groupValues[1].contains("$(")) return match.groupValues[1]
        } catch (_: IOException) {
            // unreadable: the file's name
        }

        return project.nameWithoutExtension
    }

    /** The newest build of the project's assembly under bin; null — never built. Reference assemblies are skipped. */
    fun outputOf(project: Path): Output {
        val name = assemblyNameOf(project)
        val bin = project.resolveSibling("bin")
        val wanted = "$name.dll"

        var newest: Path? = null
        var newestTime = Long.MIN_VALUE

        fun walk(directory: Path, depth: Int) {
            val entries = try {
                Files.list(directory).use { it.toList() }
            } catch (_: IOException) {
                return
            }

            for (entry in entries) {
                if (entry.isDirectory()) {
                    // bin/Debug/net10.0/ — and a runtime's folder or publish/ under it at most
                    if (depth < 5 && entry.name != "ref" && entry.name != "refint") walk(entry, depth + 1)
                } else if (entry.name.equals(wanted, ignoreCase = true)) {
                    val time = try {
                        Files.getLastModifiedTime(entry).toMillis()
                    } catch (_: IOException) {
                        continue
                    }

                    if (time > newestTime) {
                        newest = entry
                        newestTime = time
                    }
                }
            }
        }

        walk(bin, 0)

        return Output(name, bin, newest)
    }

    /** The modification time of a file in milliseconds; 0 — none. */
    fun stampOf(file: Path?): Long = try {
        if (file == null) 0 else Files.getLastModifiedTime(file).toMillis()
    } catch (_: IOException) {
        0
    }

    /** The type names a C# file declares, to find its previews among the project's. */
    fun declaredTypes(file: Path): Set<String> {
        if (!file.name.endsWith(".cs", ignoreCase = true)) return emptySet()

        return try {
            Regex("\\b(?:class|record|struct)\\s+([A-Za-z_]\\w*)").findAll(Files.readString(file)).map { it.groupValues[1] }.toSet()
        } catch (_: IOException) {
            emptySet()
        }
    }

    /** The short name of a type: after the last dot or plus. */
    fun shortName(typeName: String): String = typeName.substring(maxOf(typeName.lastIndexOf('.'), typeName.lastIndexOf('+')) + 1)
}
