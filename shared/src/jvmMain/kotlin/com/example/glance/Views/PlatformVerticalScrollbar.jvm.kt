package com.example.glance.Views

import androidx.compose.foundation.VerticalScrollbar
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.rememberScrollbarAdapter
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

@Composable
actual fun PlatformVerticalScrollbar(scrollState: LazyListState, modifier: Modifier) =
    VerticalScrollbar(rememberScrollbarAdapter(scrollState), modifier)
