// C interface to the Brain Simulator Thought Universal Knowledge Store (UKS),
// compiled from the UKS C# sources with .NET NativeAOT and linked statically into the app.
//
// Strings are UTF-8. Every function returning `char *` hands ownership to the
// caller, who must release it with uks_free(). Results are JSON; failures come
// back as {"ok":false,"error":"..."}.

#ifndef UKS_NATIVE_H
#define UKS_NATIVE_H

#ifdef __cplusplus
extern "C" {
#endif

void  uks_free(char *p);

// Lifecycle
int   uks_initialize(int clear);          // returns Thought count, -1 on failure
int   uks_thought_count(void);

// Statements
// weight in (0,1) sets the confidence of a new Link; pass 1 for the default.
char *uks_add_statement(const char *from, const char *linkType, const char *to, float weight);
char *uks_parse_line(const char *line);   // UKS text format: "[from->linkType->to] 1.00"
char *uks_delete_thought(const char *label, int withChildren);
char *uks_delete_link(const char *from, const char *linkText);

// Browsing
char *uks_get_thought(const char *label); // ThoughtDto JSON or "null"
char *uks_roots(void);                    // ["Thought", ...]
char *uks_search(const char *text, int limit);
// Neighborhood of `root` (empty = whole UKS) as {"root","nodes":[...],"edges":[...],"truncated"}
char *uks_graph(const char *root, int depth, int maxNodes);

// Query
char *uks_query(const char *source, const char *linkType, const char *target, const char *filter);
char *uks_why(const char *source, const char *linkType, const char *target);
// attributes: one "linkType,target[,weight]" per line
char *uks_search_by_attributes(const char *attributes, const char *ancestor);

// Desktop UKS dialog tree actions
char *uks_rename_thought(const char *label, const char *newLabel);
char *uks_fire_thought(const char *label);
char *uks_remove_parent(const char *child, const char *parent);

// Files
char *uks_save_xml(const char *path);
char *uks_load_xml(const char *path);
char *uks_import_text(const char *path);
char *uks_export_text(const char *root, const char *path);

// ---- Module engine (the desktop MainWindow and module dialogs) ----

// Host setup
void  uks_set_content_root(const char *path);            // folder holding UKSContent/, WordFIles/, Resources/
void  uks_set_setting(const char *key, const char *value); // app.config appSettings, e.g. "APIKey"
void  uks_set_midi_callback(void (*callback)(int message)); // NAudio short messages: status | d1<<8 | d2<<16
// decode(path, rgba, capacity, &w, &h): called with rgba == NULL for the size first; return 1 on success
void  uks_set_image_decoder(int (*decode)(const char *path, unsigned char *rgba, int capacity, int *w, int *h));
char *uks_notices(void);                                  // [{"title","text"}] raised since the last call

// Engine: fires every active module each cycle on a background thread (0 stops it)
void  uks_engine_run(int intervalMs);
long long uks_engine_ticks(void);

// Modules
char *uks_modules_available(void);                        // ["ModuleAction", ...]
char *uks_modules_active(void);                           // [{"label","type","enabled","revision"}]
char *uks_module_activate(const char *type);              // message = new module label
char *uks_module_remove(const char *label);
char *uks_module_set_enabled(const char *label, int enabled);
char *uks_module_initialize(const char *label);
// A module's logic interface (Host/ModuleApi.cs); its SwiftUI view is the dialog.
// args is a JSON object ("" for none); results are JSON, or {"ok":false,"error":…}.
char *uks_module_query(const char *label, const char *op, const char *args);  // read-only: "state", …; "busy" if the UKS is
char *uks_module_call(const char *label, const char *op, const char *args);   // an operation: "present", "turn", …

#ifdef __cplusplus
}
#endif

#endif
