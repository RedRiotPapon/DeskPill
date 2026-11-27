# Music source and volume

Clicking the artwork/title in Music returns to the app identified by the current Windows media session. Playback buttons, seeking, lyrics and page navigation keep their existing actions.

## Browser tabs

DeskPill handles Chrome, Edge, Brave, Vivaldi, Opera, Firefox and Chromium source IDs. It inspects native accessible tab labels only when the user clicks the track. It skips web documents, reads no URLs or browser profile files, installs no extension and opens no debugging port. The traversal has time, node, depth and sibling limits; incomplete or failed scans cannot claim a unique match.

Windows exposes a [source application ID](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.sourceappusermodelid) for a media session. DeskPill matches its title and artist to tab labels, then uses [UI Automation selection](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.selectionitempattern.select). Titles normalize case and punctuation while preserving Unicode letters and combining marks. A full artist name can distinguish two matching titles. Equal-scoring matches remain ambiguous. Short song names require an exact tab label or a matching artist.

When a complete scan has one best match, DeskPill selects that tab. Otherwise it brings the browser forward without choosing a tab. A browser may hide its tab strip, customize media metadata, or decline accessibility access; profile/app windows and elevated processes can also limit matching. Selection is best effort, rather than a browser-internal media-tab ID. Cancellation and foreground checks prevent a slow lookup from selecting a tab after timeout or after the user switches apps. Registered desktop/packaged sources can activate through Windows Shell; metadata never becomes an executable command line.

A read-only check of the current Chrome media session found its matching accessible tab locally. An isolated WPF tab fixture verifies native lookup, selection and incomplete-scan fallback. The other browsers have deterministic source-ID coverage but still need live browser checks.

## Volume

The volume flyout uses a themed continuous slider, with pointer seeking, thumb dragging, arrow keys and an accessible name. The thumb and percentage update immediately. A single owner-scoped background queue coalesces adjacent volume requests, serializes mute toggles and preserves the latest value across flyout reopening. Windows callbacks update the view without writing back; stale callbacks cannot pull the thumb away during a drag or pending write.

The flyout dismisses after 2.5 seconds of inactivity, 450 ms after pointer exit, on outside click, Esc, page replacement, notch collapse, hiding or shutdown. It stays open during a drag, including dragging outside its bounds. Controls follow the selected theme; Classic's music layout is preserved.

Deterministic checks cover browser aliases, title/artist matching, ambiguity, Unicode, cancellation and audio queue ordering. Interactive fixtures cover native accessibility selection, scan completeness, drag/idle dismissal, external volume synchronization and orphan-popup cleanup. They do not change the user's real volume or switch the user's browser tabs.
