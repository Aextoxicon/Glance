import java.nio.file.Files
import java.nio.file.StandardCopyOption
import org.jetbrains.compose.desktop.application.dsl.TargetFormat

plugins {
    alias(libs.plugins.kotlinJvm)
    alias(libs.plugins.composeMultiplatform)
    alias(libs.plugins.composeCompiler)
}

val nativeLibDir = layout.buildDirectory.dir("nativeLibs/jvm")

val hostResourceSubDir = when {
    System.getProperty("os.name").lowercase().contains("win") -> "windows-x64"
    System.getProperty("os.name").lowercase().contains("mac") ->
        if (System.getProperty("os.arch").lowercase().contains("aarch64") ||
            System.getProperty("os.arch").lowercase().contains("arm64")) "macos-arm64"
        else "macos-x64"
    else -> "linux-x64"
}
val nativeLibResourceDir = nativeLibDir.map { it.dir(hostResourceSubDir) }

val copyDesktopNativeLib by tasks.registering(Copy::class) {
    dependsOn(":shared:copyJvmNativeLib")
    from(project(":shared").layout.buildDirectory.dir("nativeLibs/jvm"))
    into(nativeLibResourceDir)
}

dependencies {
    implementation(project(":shared"))

    implementation(compose.desktop.currentOs)
    implementation(libs.kotlinx.coroutinesSwing)

    implementation(libs.compose.uiToolingPreview)
}

compose.desktop {
    application {
        mainClass = "com.example.glance.MainKt"

        jvmArgs("-Xshare:auto")
        jvmArgs("-XX:SharedArchiveFile=\$APPDIR/resources/app.jsa")

        nativeDistributions {
            targetFormats(TargetFormat.Dmg, TargetFormat.Msi, TargetFormat.Deb, TargetFormat.Exe)
            packageName = "com.example.glance"
            packageVersion = "1.0.0"
            appResourcesRootDir.set(nativeLibDir)
        }
    }
}

tasks.matching {
    it.name == "createDistributable" ||
        it.name.startsWith("package") ||
        it.name == "prepareAppResources"
}.configureEach {
    dependsOn(copyDesktopNativeLib)
}

tasks.withType<JavaExec>().configureEach {
    dependsOn(copyDesktopNativeLib)
    val nativeLibPath = nativeLibResourceDir.get().asFile.absolutePath
    systemProperty("jna.library.path", nativeLibPath)
    systemProperty("java.library.path", nativeLibPath)
    environment("DYLD_LIBRARY_PATH", nativeLibPath)
    environment("PATH", nativeLibPath + File.pathSeparator + (System.getenv("PATH") ?: ""))
}

val generateAppCdsArchive by tasks.registering {
    group = "distribution"
    description = "生成 AppCDS 共享类归档并放入应用 resources"
    dependsOn("createDistributable")
    val appImageDirProvider = layout.buildDirectory.dir("compose/binaries/main/app/com.example.glance")
    val appDirProvider = appImageDirProvider.map { it.dir("app") }
    val cfgProvider = appDirProvider.map { it.file("com.example.glance.cfg") }
    val resourcesProvider = appDirProvider.map { it.dir("resources") }
    val jsaProvider = resourcesProvider.map { it.file("app.jsa") }
    inputs.file(cfgProvider)
    inputs.dir(appDirProvider)
    outputs.file(jsaProvider)

    val classListPath = layout.buildDirectory.get().asFile.resolve("cds/classes.lst").absolutePath
    val javaBin = file(System.getProperty("java.home")).resolve("bin/java.exe").toString()
    val scriptPath = layout.projectDirectory.dir("scripts").file("generate-app-cds.ps1").asFile.absolutePath
    val nativeLibPath = nativeLibResourceDir.get().asFile.absolutePath
    doLast {
        val appDir = appDirProvider.get().asFile

        val classpath = cfgProvider.get().asFile.readLines()
            .filter { it.startsWith("app.classpath=") }
            .map { it.removePrefix("app.classpath=\$APPDIR\\") }
            .joinToString(";") { appDir.resolve(it).absolutePath }
        val jsaOut = jsaProvider.get().asFile.absolutePath

        val runtimeJava = appImageDirProvider.get().asFile.resolve("runtime/bin/java.exe")
        runtimeJava.parentFile.mkdirs()

        var lastErr: Exception? = null
        for (attempt in 1..5) {
            try {
                Files.copy(
                    File(javaBin).toPath(), runtimeJava.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                )
                lastErr = null
                break
            } catch (e: Exception) {
                lastErr = e
                Thread.sleep(1000)
            }
        }
        check(lastErr == null) { "复制 java.exe 到 runtime\\bin 失败: $lastErr" }
        val cmd = listOf(
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
            "-JavaBin", runtimeJava.absolutePath,
            "-ClassPath", classpath,
            "-ResourcesDir", resourcesProvider.get().asFile.absolutePath,
            "-SkikoPath", appDir.absolutePath,
            "-JsaOut", jsaOut,
            "-ClassListPath", classListPath,
        )
        val proc = ProcessBuilder(cmd).inheritIO().start()
        check(proc.waitFor() == 0) { "AppCDS 生成脚本执行失败" }
        check(File(jsaOut).exists()) { "AppCDS 归档未生成: $jsaOut" }

        File(jsaOut).copyTo(File(nativeLibPath, "app.jsa"), overwrite = true)
    }
}