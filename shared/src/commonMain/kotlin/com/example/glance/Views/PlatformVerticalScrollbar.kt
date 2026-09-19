package com.example.glance.Views

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

@Composable
expect fun PlatformVerticalScrollbar(scrollState: LazyListState, modifier: Modifier)
