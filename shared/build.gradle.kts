import org.jetbrains.kotlin.gradle.dsl.JvmTarget

val nativeProjectDir = rootProject.file("native")
val nativeTargetDir = nativeProjectDir.resolve("target")
val nativeLibName = "uniffi_code_parser"

// JVM/桌面端按平台区分native库文件名（cargo产物命名规则）
val hostOs = System.getProperty("os.name").lowercase()
val jvmNativeLib = when {
    hostOs.contains("mac") -> nativeTargetDir.resolve("debug/lib${nativeLibName}.dylib")
    hostOs.contains("win") -> nativeTargetDir.resolve("debug/${nativeLibName}.dll")
    else -> nativeTargetDir.resolve("debug/lib${nativeLibName}.so")
}

val uniffiKotlinOutDir = layout.buildDirectory.dir("generated/uniffi/kotlin")

val cargoBuildJvm by tasks.registering(Exec::class) {
    group = "uniffi"
    description = "Build Rust native library for JVM (debug)"
    workingDir = nativeProjectDir
    commandLine("cargo", "build")
    inputs.dir(nativeProjectDir.resolve("src"))
    inputs.file(nativeProjectDir.resolve("Cargo.toml"))
    outputs.file(jvmNativeLib)
}

val jvmNativeLibOutputDir = layout.buildDirectory.dir("nativeLibs/jvm")
val copyJvmNativeLib by tasks.registering(Copy::class) {
    group = "uniffi"
    description = "Copy JVM native library to build output"
    dependsOn(cargoBuildJvm)
    from(jvmNativeLib)
    into(jvmNativeLibOutputDir)
}

// AGP自动打包进APK
val androidJniLibsDir = layout.projectDirectory.dir("src/androidMain/jniLibs")
val cargoBuildAndroid by tasks.registering(Exec::class) {
    group = "uniffi"
    description = "Build Rust native library for Android (arm64-v8a, armeabi-v7a, x86_64)"
    workingDir = nativeProjectDir
    commandLine(
        "cargo", "ndk",
        "-t", "arm64-v8a",
        "-t", "armeabi-v7a",
        "-t", "x86_64",
        "-o", androidJniLibsDir.asFile.absolutePath,
        "build"
    )
    inputs.dir(nativeProjectDir.resolve("src"))
    inputs.file(nativeProjectDir.resolve("Cargo.toml"))
    outputs.dir(androidJniLibsDir)
}

val buildNativeLibs by tasks.registering {
    group = "uniffi"
    description = "Build and copy Rust native libraries for all platforms (JVM + Android)"
    dependsOn(copyJvmNativeLib, cargoBuildAndroid)
}

// jvm和android共用
val generateUniffiKotlinBindings by tasks.registering(Exec::class) {
    group = "uniffi"
    description = "Generate Kotlin Multiplatform bindings from Rust library using uniffi-bindgen"
    dependsOn(cargoBuildJvm)
    workingDir = nativeProjectDir
    val outDir = uniffiKotlinOutDir.get().asFile
    val nativeLibPath = jvmNativeLib.absolutePath
    outputs.dir(outDir)
    doFirst {
        outDir.mkdirs()
    }
    commandLine(
        "uniffi-bindgen", "generate",
        "--library", nativeLibPath,
        "--language", "kotlin",
        "--out-dir", outDir.absolutePath
    )
}

plugins {
    alias(libs.plugins.kotlinMultiplatform)
    alias(libs.plugins.androidMultiplatformLibrary)
    alias(libs.plugins.composeMultiplatform)
    alias(libs.plugins.composeCompiler)
}

kotlin {
    jvm()

    android {
        namespace = "com.example.glance.shared"
        compileSdk = libs.versions.android.compileSdk.get().toInt()
        minSdk = libs.versions.android.minSdk.get().toInt()

        compilerOptions {
            jvmTarget = JvmTarget.JVM_11
        }
        androidResources {
            enable = true
        }
        withHostTest {
            isIncludeAndroidResources = true
        }
        withDeviceTestBuilder {
            sourceSetTreeName = "test"
        }.configure {
            instrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        }
    }

    sourceSets {
        val generatedDir = uniffiKotlinOutDir.get().asFile

        commonMain {
            dependencies {
                implementation(libs.compose.runtime)
                implementation(libs.compose.foundation)
                implementation(libs.compose.material3)
                implementation(libs.compose.ui)
                implementation(libs.compose.components.resources)
                implementation(libs.compose.uiToolingPreview)
                implementation(libs.androidx.lifecycle.viewmodelCompose)
                implementation(libs.androidx.lifecycle.runtimeCompose)
                implementation(libs.compose.materialIconsCore)
                implementation(libs.compose.materialIconsExtended)
            }
        }
        jvmMain {
            kotlin.srcDir(generatedDir)
            dependencies {
                implementation(libs.jna)
                implementation(libs.jna.platform)
            }
        }
        androidMain {
            kotlin.srcDir(generatedDir)
            dependencies {
                implementation(libs.jna)
                implementation(libs.jna.platform)
                implementation(libs.compose.uiToolingPreview)
                implementation(libs.androidx.documentfile)
            }
        }
        commonTest.dependencies {
            implementation(libs.kotlin.test)
            implementation(libs.kotlinx.coroutines.test)
            implementation(libs.compose.ui.test)
            implementation(libs.compose.ui.test.junit4)
        }
        jvmTest {
            dependencies {
                implementation(compose.desktop.currentOs)
            }
        }
    }
}

tasks.named("compileKotlinJvm") {
    dependsOn(generateUniffiKotlinBindings)
}

tasks.named("compileAndroidMain") {
    dependsOn(generateUniffiKotlinBindings)
}

tasks.named("compileTestKotlinJvm") {
    dependsOn(generateUniffiKotlinBindings)
}

tasks.withType<Test>().configureEach {
    dependsOn(cargoBuildJvm)
    systemProperty("uniffi.component.$nativeLibName.libraryOverride", jvmNativeLib.absolutePath)
    systemProperty("java.library.path", jvmNativeLib.parentFile.absolutePath)
}

tasks.named("assemble") {
    dependsOn(copyJvmNativeLib)
}

// 必须等待cargo ndk产物就绪
tasks.matching { it.name.contains("JniLibFolders") }.configureEach {
    dependsOn(cargoBuildAndroid)
}

tasks.named("build") {
    dependsOn(buildNativeLibs)
}

val testApp by tasks.registering {
    group = "verification"
    description = "Run UI golden tests (JVM now; add testDebugUnitTest when Android is ready)"
    dependsOn("jvmTest")
}