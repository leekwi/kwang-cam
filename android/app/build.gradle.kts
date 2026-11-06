plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.kwangssam.kwangcam"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.kwangssam.kwangcam"
        minSdk = 26
        targetSdk = 34
        versionCode = (System.getenv("KWANGCAM_VERSION_CODE") ?: "1").toInt()
        versionName = System.getenv("KWANGCAM_VERSION_NAME") ?: "0.1.0"
    }

    // CI에 서명 키(KWANGCAM_KEYSTORE)가 있으면 그 키로, 없으면 디버그 키로 서명합니다.
    // 항상 같은 키로 서명해야 폰에서 앱을 지우지 않고 업데이트할 수 있어요.
    val releaseKeystore = System.getenv("KWANGCAM_KEYSTORE")?.let { file(it) }?.takeIf { it.exists() }
    signingConfigs {
        if (releaseKeystore != null) {
            create("release") {
                storeFile = releaseKeystore
                storePassword = System.getenv("KWANGCAM_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("KWANGCAM_KEY_ALIAS")
                keyPassword = System.getenv("KWANGCAM_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName(if (releaseKeystore != null) "release" else "debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        viewBinding = true
    }
}

dependencies {
    val camerax = "1.3.4"
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.constraintlayout:constraintlayout:2.1.4")
    implementation("androidx.camera:camera-core:$camerax")
    implementation("androidx.camera:camera-camera2:$camerax")
    implementation("androidx.camera:camera-lifecycle:$camerax")
    implementation("androidx.camera:camera-view:$camerax")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
}
