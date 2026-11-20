plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("kotlin-kapt")
}


val envVersionCode = System.getenv("ANDROID_VERSION_CODE")?.toIntOrNull()
val envVersionName = System.getenv("ANDROID_VERSION_NAME")
val releaseKeystorePath = System.getenv("ANDROID_KEYSTORE_PATH")
val releaseKeystorePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
val releaseKeyAlias = System.getenv("ANDROID_KEY_ALIAS")
val releaseKeyPassword = System.getenv("ANDROID_KEY_PASSWORD")
val hasReleaseSigning = !releaseKeystorePath.isNullOrBlank() &&
    !releaseKeystorePassword.isNullOrBlank() &&
    !releaseKeyAlias.isNullOrBlank() &&
    !releaseKeyPassword.isNullOrBlank()

val apiBaseUrl = (System.getenv("CWI_API_BASE_URL")
    ?: (project.findProperty("CWI_API_BASE_URL") as String?))
    ?.trim()
    ?: ""

val packName = (System.getenv("CWI_PACK_NAME")
    ?: (project.findProperty("CWI_PACK_NAME") as String?))
    ?.trim()
    ?: "default"

android {
    namespace = "com.cwi.personal"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.cwi.personal"
        minSdk = 26
        targetSdk = 34
        versionCode = envVersionCode ?: 1
        versionName = envVersionName ?: "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField("String", "API_BASE_URL", "\"${apiBaseUrl}\"")
        buildConfigField("String", "PACK_NAME", "\"${packName}\"")
        buildConfigField("String", "CERT_PIN_HOST", "\"\"")
        buildConfigField("String", "CERT_PIN_SHA256", "\"\"")
        buildConfigField("Double", "INTERACTION_SAMPLE_RATE", "1.0")
        buildConfigField("int", "INTERACTION_QUEUE_LIMIT", "200")
        buildConfigField("int", "FEEDBACK_QUEUE_LIMIT", "100")
    }


    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = file(releaseKeystorePath!!)
                storePassword = releaseKeystorePassword
                keyAlias = releaseKeyAlias
                keyPassword = releaseKeyPassword
            }
        }
    }

    buildTypes {
        debug {
            buildConfigField("Boolean", "LOG_HTTP", "true")
            buildConfigField("Boolean", "TELEMETRY_ENABLED", "true")
            buildConfigField("Boolean", "SIMULATE_OFFLINE", "false")
            buildConfigField("Long", "SIMULATE_BACKPRESSURE_MS", "0L")
            buildConfigField("Boolean", "CERT_PINNING_ENABLED", "false")
        }
        release {
            if (hasReleaseSigning) {
                signingConfig = signingConfigs.getByName("release")
            }
            isMinifyEnabled = false
            buildConfigField("Boolean", "LOG_HTTP", "false")
            buildConfigField("Boolean", "TELEMETRY_ENABLED", "false")
            buildConfigField("Boolean", "SIMULATE_OFFLINE", "false")
            buildConfigField("Long", "SIMULATE_BACKPRESSURE_MS", "0L")
            buildConfigField("Boolean", "CERT_PINNING_ENABLED", "true")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    kapt {
        correctErrorTypes = true
    }

    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.8"
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
    implementation("androidx.activity:activity-compose:1.9.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.7.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.7.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.7.0")
    implementation("androidx.room:room-runtime:2.6.1")
    implementation("androidx.room:room-ktx:2.6.1")
    kapt("androidx.room:room-compiler:2.6.1")
    implementation("androidx.security:security-crypto:1.1.0-alpha06")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")
    implementation("com.squareup.retrofit2:retrofit:2.11.0")
    implementation("com.squareup.retrofit2:converter-moshi:2.11.0")
    implementation("com.squareup.moshi:moshi-kotlin:1.15.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.8.1")
    androidTestImplementation("androidx.test.ext:junit:1.1.5")
    androidTestImplementation("androidx.test:core:1.5.0")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.5.1")
    androidTestImplementation(platform("androidx.compose:compose-bom:2024.06.00"))
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    androidTestImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.8.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}


