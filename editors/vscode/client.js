const { workspace, window } = require('vscode');

const clients = [];

/**
 * Loads the language client library.
 *
 * The library is a dependency fetched with npm. If it is missing the extension
 * still contributes syntax highlighting and the language configuration for
 * both languages, so editing works; only the server-backed features are
 * unavailable.
 */
function loadLanguageClient() {
    try {
        return require('vscode-languageclient/node');
    } catch (error) {
        window.showWarningMessage(
            'cacalang & CIL: run "npm install" in editors/vscode to enable live errors and (for cacalang) hover, ' +
            'go to definition and find references. Syntax highlighting works without it.');
        return undefined;
    }
}

/**
 * One entry per language this extension speaks LSP for. cacalang's server
 * (Caca.LanguageServer) lives in this repository; CIL's (Caca.VM.LanguageServer)
 * lives in CacaVM (github.com/Arawn-Davies/CacaVM), a separate repository this
 * extension does not vendor — see the top-level README's "Combined with CacaVM"
 * section for how the two fit together.
 */
const LANGUAGES = [
    {
        id: 'caca',
        name: 'cacalang',
        settingsSection: 'cacalang',
        defaultCommand: 'caca-langserver',
        publishHint: "'dotnet publish src/Caca.LanguageServer -c Release -o artifacts/langserver'",
        // Assemblies extern functions may bind to, matching the command
        // line's --ref. Read once at startup: reload the window after
        // changing the setting or building a listed assembly.
        extraInitializationOptions: () => ({
            references: (workspace.getConfiguration('cacalang').get('references') || [])
                .map((reference) => reference.replace(/\$\{workspaceFolder\}/g, workspaceRoot())),
        }),
    },
    {
        id: 'cil',
        name: 'CIL',
        settingsSection: 'cil',
        defaultCommand: 'cil-langserver',
        publishHint: "'dotnet publish source/Caca.VM.LanguageServer -c Release -o artifacts/langserver' " +
            '(from a CacaVM checkout)',
        extraInitializationOptions: () => ({}),
    },
];

/**
 * Starts one language client per entry in LANGUAGES, each connected to its
 * own file extension and each resolving its own server independently — a
 * missing or unconfigured CIL server does not stop cacalang's from starting,
 * and vice versa.
 *
 * Every server is a .NET executable that speaks LSP over stdin and stdout, so
 * there is nothing to run here beyond pointing each client at its own.
 */
function activate(context) {
    const languageClient = loadLanguageClient();

    if (!languageClient) {
        return;
    }

    for (const language of LANGUAGES) {
        startClient(languageClient, language, context);
    }
}

function startClient(languageClient, language, context) {
    const { LanguageClient, TransportKind } = languageClient;
    const server = resolveServer(language);

    const execution = {
        command: server.command,
        args: server.args,
        transport: TransportKind.stdio,
        options: { env: { ...process.env, ...server.env } },
    };

    const serverOptions = { run: execution, debug: execution };

    const clientOptions = {
        documentSelector: [{ scheme: 'file', language: language.id }],
        synchronize: {
            fileEvents: workspace.createFileSystemWatcher(`**/*.${language.id}`),
        },
        initializationOptions: language.extraInitializationOptions(),
    };

    const client = new LanguageClient(language.id, language.name, serverOptions, clientOptions);

    client.start().catch((error) => {
        window.showErrorMessage(
            `${language.name}: could not start '${server.command}'. Build it with ${language.publishHint} and ` +
            `set ${language.settingsSection}.server.path. (${error.message})`);
    });

    clients.push(client);
    context.subscriptions.push(client);
}

/**
 * Works out how to start a language server.
 *
 * The configured path may use ${workspaceFolder}, so a setting committed to a
 * repository works for anyone who builds the server into it.
 *
 * A .dll is run through `dotnet`, which is found on PATH. The native launcher
 * beside it is not used by default: it locates the runtime through the standard
 * install locations and an explicit DOTNET_ROOT, neither of which covers a .NET
 * installed under the user's home directory, so it fails to start in exactly
 * the setup where an editor is most likely to launch it.
 */
/** The first workspace folder, which ${workspaceFolder} in settings expands to. */
function workspaceRoot() {
    return workspace.workspaceFolders && workspace.workspaceFolders.length > 0
        ? workspace.workspaceFolders[0].uri.fsPath
        : '';
}

function resolveServer(language) {
    const fs = require('fs');
    const path = require('path');
    const os = require('os');

    const configured = workspace.getConfiguration(language.settingsSection).get('server.path');
    const expanded = (configured || '').replace(/\$\{workspaceFolder\}/g, workspaceRoot());
    const env = dotnetRoot() ? { DOTNET_ROOT: dotnetRoot() } : {};

    if (expanded.length === 0) {
        return { command: language.defaultCommand, args: [], env };
    }

    if (expanded.endsWith('.dll')) {
        if (!fs.existsSync(expanded)) {
            reportMissingServer(language, expanded);
        }

        return { command: 'dotnet', args: [expanded], env };
    }

    for (const candidate of [expanded, expanded + '.exe']) {
        if (fs.existsSync(candidate)) {
            return { command: candidate, args: [], env };
        }
    }

    reportMissingServer(language, expanded);
    return { command: language.defaultCommand, args: [], env };

    /** A .NET installed under the home directory, which the launcher will not find on its own. */
    function dotnetRoot() {
        const home = path.join(os.homedir(), '.dotnet');
        return fs.existsSync(path.join(home, 'dotnet')) ? home : undefined;
    }
}

function reportMissingServer(language, expected) {
    window.showWarningMessage(
        `${language.name}: no language server at ${expected}. Build it with ${language.publishHint}.`);
}

function deactivate() {
    return Promise.all(clients.map((client) => client.stop()));
}

module.exports = { activate, deactivate };
