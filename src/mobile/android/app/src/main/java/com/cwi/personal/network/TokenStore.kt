package com.cwi.personal.network

import android.content.Context
import com.cwi.personal.security.SecureTokenStore

class TokenStore(context: Context) {
    private val secureStore = SecureTokenStore(context)

    @Volatile
    var token: String? = secureStore.getToken()
        set(value) {
            field = value
            secureStore.setToken(value)
        }
}
