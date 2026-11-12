# Conversation UX Refinement v2 Design

## Scope

Change only the Conversation module and its AI Workspace presentation. Do not change AI Engine, DeepThink, streaming, the chat request pipeline, message rendering, or the composer.

## Conversation panel

- Keep the existing conversation-panel visibility state and toggle behavior.
- Move `AIConversationPanel` from the left column of `ConversationView` to the right column.
- Keep the primary Nexus sidebar outside `ConversationView` unchanged.

## Local grouping and display

- `ConversationDetailRecord.UpdatedAt` and `ConversationIndexItem.UpdatedAt` remain UTC and are never rewritten for display or grouping.
- Convert `UpdatedAt` to local time only in the conversation-list presentation model.
- Expose an `ICollectionView` over the flat conversation collection.
- Sort items by original `UpdatedAt` descending before grouping.
- Group by the local calendar date relative to the user's current local date:
  - `Hari Ini`: updated today.
  - `Kemarin`: updated yesterday.
  - `7 Hari Terakhir`: updated 2 through 7 calendar days ago.
  - `Lebih Lama`: updated more than 7 calendar days ago.
- Use `HH:mm` with the current user culture for `Hari Ini` and `Kemarin`.
- Use `dd MMM` with the current user culture for the other groups.
- Search continues to filter titles without changing timestamps or group assignment.

## Rename

- Right-clicking a conversation exposes `Rename` and `Delete`.
- `Rename` switches only that row to inline editing and selects the current title.
- Enter trims and saves a non-empty title.
- Escape restores the original title and exits editing without persistence.
- Rename updates the detail title and index title but preserves the exact existing `UpdatedAt` value.
- The existing storage save path receives an explicit option to preserve `UpdatedAt`; normal message persistence retains its current timestamp-update behavior.

## Delete

- `Delete` first displays a lightweight confirmation.
- No reusable Nexus confirmation dialog currently exists in the source, so v2 uses WPF `MessageBox` as the approved fallback.
- The conversation JSON file is moved through the Windows Recycle Bin API.
- If recycling fails, the exception is shown to the user; the file, index entry, list item, and active conversation state remain intact.
- Permanent deletion is forbidden. The existing `File.Delete` fallback is removed.
- After successful deletion, the list is refreshed. Deleting the active conversation opens the newest remaining conversation or clears the chat display if none remain.

## Chat area

- Replace the large bordered `AIChatPanelBorder` wrapper with a transparent grid occupying the available chat area.
- Remove its background, border, corner radius, and restrictive maximum width.
- Preserve the welcome panel, message items control, bubble template, streaming bindings, composer, and all chat commands unchanged.

## Implementation boundaries

- Extend existing Conversation storage and manager contracts; add no service or architecture layer.
- Keep confirmation/focus/key handling in `ConversationView.xaml.cs`, while persistence, grouping state, and rename state stay in existing Conversation/ViewModel types.
- Do not modify llama-server configuration, llama client payloads, DeepThink state, or streaming code.

## Verification

- Unit-test UTC preservation and local date boundaries for all four groups.
- Unit-test descending order within groups and local display formats.
- Unit-test rename persistence with unchanged `UpdatedAt`.
- Unit-test recycle failure leaves the source file intact and propagates the error.
- Integration-test the `ICollectionView`, inline Enter/Escape behavior, right-side panel placement, context-menu entries, and absence of the large chat border.
- Build the solution, run Conversation and integration tests, then run the full regression suite serially.
