using System.Text;
using Lms.IntegrationTests.Packages.Corpus;

namespace Lms.IntegrationTests.Packages.Demo;

/// <summary>
/// The packages of the M4b E2E demos (Task 23), written by us from scratch: no third-party code, text or assets, so there is no licence to
/// carry. Every file is generated here (stored entries, fixed timestamps: the bytes are identical on every machine) and committed under
/// <c>files/</c>; <c>DemoPackageTests</c> proves the committed files equal the builder and that they pass the archive reader and the real validators.
/// The pages carry stable <c>data-testid</c> attributes (listed in <c>files/README.md</c>) for the page objects of the E2E suite.
/// </summary>
public static class DemoPackages
{
    public const string Scorm12File = "demo-scorm12.zip";

    public const string Scorm2004File = "demo-scorm2004.zip";

    public const string H5pFile = "demo-h5p-quiz.h5p";

    public static IReadOnlyDictionary<string, Func<byte[]>> All { get; } = new Dictionary<string, Func<byte[]>>
    {
        [Scorm12File] = Scorm12,
        [Scorm2004File] = Scorm2004,
        [H5pFile] = H5pQuiz,
    };

    private static string L(string text) => text.ReplaceLineEndings("\n");

    private static RawEntry F(string name, string text) => RawZipWriter.File(name, Encoding.UTF8.GetBytes(L(text)));

    // ---- SCORM -----------------------------------------------------------------------------------------------------------------------------

    private static string Manifest12() => L("""
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="lms-demo-scorm12" version="1.0" xmlns="http://www.imsproject.org/xsd/imscp_rootv1p1p2" xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_rootv1p2">
          <metadata><schema>ADL SCORM</schema><schemaversion>1.2</schemaversion></metadata>
          <organizations default="org">
            <organization identifier="org"><title>LMS Demo SCORM 1.2</title>
              <item identifier="item1" identifierref="res1"><title>LMS Demo SCORM 1.2</title></item>
            </organization>
          </organizations>
          <resources>
            <resource identifier="res1" type="webcontent" adlcp:scormtype="sco" href="index.html">
              <file href="index.html"/><file href="page2.html"/><file href="sco.js"/><file href="style.css"/>
            </resource>
          </resources>
        </manifest>
        """);

    private static string Manifest2004() => L("""
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="lms-demo-scorm2004" version="1.0" xmlns="http://www.imsglobal.org/xsd/imscp_v1p1" xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_v1p3">
          <metadata><schema>ADL SCORM</schema><schemaversion>2004 4th Edition</schemaversion></metadata>
          <organizations default="org">
            <organization identifier="org"><title>LMS Demo SCORM 2004</title>
              <item identifier="item1" identifierref="res1"><title>LMS Demo SCORM 2004</title></item>
            </organization>
          </organizations>
          <resources>
            <resource identifier="res1" type="webcontent" adlcp:scormType="sco" href="index.html">
              <file href="index.html"/><file href="page2.html"/><file href="sco.js"/><file href="style.css"/>
            </resource>
          </resources>
        </manifest>
        """);

    private const string Css = """
        body { font-family: sans-serif; margin: 2rem; max-width: 40rem; }
        button, input { font-size: 1rem; padding: .4rem .8rem; }
        [data-testid="sco-api-status"] { color: #555; }
        """;

    private static string Page1(string title) => $"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>{title}: page 1</title><link rel="stylesheet" href="style.css"></head>
        <body>
        <h1 data-testid="sco-title">{title}</h1>
        <p data-testid="sco-page-1">This is page 1 of the demo lesson.</p>
        <p data-testid="sco-api-status">Looking for the API…</p>
        <button type="button" data-testid="sco-next" id="next">Next page</button>
        <script src="sco.js"></script>
        <script>LmsDemo.page1();</script>
        </body>
        </html>
        """;

    private static string Page2(string title) => $"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>{title}: page 2</title><link rel="stylesheet" href="style.css"></head>
        <body>
        <h1 data-testid="sco-title">{title}</h1>
        <p data-testid="sco-page-2">This is page 2 of the demo lesson.</p>
        <p data-testid="sco-api-status">Looking for the API…</p>
        <label>Score <input type="number" data-testid="sco-score" id="score" min="0" max="100" value="85"></label>
        <button type="button" data-testid="sco-finish" id="finish">Finish</button>
        <p data-testid="sco-result" id="result"></p>
        <script src="sco.js"></script>
        <script>LmsDemo.page2();</script>
        </body>
        </html>
        """;

    /// <summary>Shared by both versions: only the calls and keys differ (a small table at the top).</summary>
    private static string ScoJs(bool v2004) => L($$"""
        // LMS demo SCO runtime (authored by the LMS team). Finds the API the platform provides, never contacts anything else.
        var LmsDemo = (function () {
          var V2004 = {{(v2004 ? "true" : "false")}};
          var api = null, ready = false;
          function find() {
            // The platform defines the API on this window; a classic parent walk is tried as well and its SecurityError is tolerated.
            api = V2004 ? window.API_1484_11 : window.API;
            if (!api) { try { var w = window.parent; api = V2004 ? w.API_1484_11 : w.API; } catch (e) { api = null; } }
            return api;
          }
          function call(name12, name2004) { return V2004 ? api[name2004] : api[name12]; }
          function init() {
            var status = document.querySelector('[data-testid="sco-api-status"]');
            if (!find()) { status.textContent = 'API NOT found'; return false; }
            status.textContent = 'API found (' + (V2004 ? 'SCORM 2004' : 'SCORM 1.2') + ')';
            ready = String(call('LMSInitialize', 'Initialize')('')) === 'true';
            return ready;
          }
          function get(key) { return String(call('LMSGetValue', 'GetValue')(key) || ''); }
          function set(key, value) { return call('LMSSetValue', 'SetValue')(key, String(value)); }
          function commit() { return call('LMSCommit', 'Commit')(''); }
          function finish() { return call('LMSFinish', 'Terminate')(''); }
          return {
            page1: function () {
              if (!init()) { return; }
              // A returning learner continues on page 2 (the position is kept in suspend_data).
              if (get('cmi.suspend_data') === 'page=2') { location.replace('page2.html'); return; }
              document.getElementById('next').addEventListener('click', function () {
                set('cmi.suspend_data', 'page=2');
                set(V2004 ? 'cmi.completion_status' : 'cmi.core.lesson_status', 'incomplete');
                commit();
                // The commit travels to the platform asynchronously; the next page asks the server for its state when it loads.
                setTimeout(function () { location.href = 'page2.html'; }, 600);
              });
            },
            page2: function () {
              if (!init()) { return; }
              document.getElementById('finish').addEventListener('click', function () {
                var score = Number(document.getElementById('score').value);
                if (V2004) {
                  set('cmi.score.raw', score); set('cmi.score.min', 0); set('cmi.score.max', 100); set('cmi.score.scaled', score / 100);
                  set('cmi.completion_status', 'completed'); set('cmi.success_status', score >= 60 ? 'passed' : 'failed');
                } else {
                  set('cmi.core.score.raw', score); set('cmi.core.score.min', 0); set('cmi.core.score.max', 100);
                  set('cmi.core.lesson_status', score >= 60 ? 'passed' : 'failed');
                }
                set('cmi.suspend_data', 'page=2');
                commit();
                finish();
                document.getElementById('result').textContent = 'Finished with score ' + score;
                document.getElementById('finish').disabled = true;
              });
            }
          };
        })();
        """);

    private static byte[] Scorm(string manifest, string title, bool v2004) => RawZipWriter.Write(
    [
        F("imsmanifest.xml", manifest),
        F("index.html", Page1(title)),
        F("page2.html", Page2(title)),
        F("sco.js", ScoJs(v2004)),
        F("style.css", Css),
    ]);

    public static byte[] Scorm12() => Scorm(Manifest12(), "LMS Demo SCORM 1.2", false);

    public static byte[] Scorm2004() => Scorm(Manifest2004(), "LMS Demo SCORM 2004", true);

    // ---- H5P -------------------------------------------------------------------------------------------------------------------------------

    private const string QuizLibrary = "LmsDemoQuiz";

    private static string QuizJs() => L("""
        /* LmsDemoQuiz: a one-question quiz library authored by the LMS team for the E2E demo (no third-party code). */
        var H5P = H5P || {};
        H5P.LmsDemoQuiz = (function () {
          function Quiz(options, contentId) {
            H5P.EventDispatcher.call(this);
            this.options = options || {};
            this.contentId = contentId;
          }
          Quiz.prototype = Object.create(H5P.EventDispatcher.prototype);
          Quiz.prototype.constructor = Quiz;
          Quiz.prototype.getMaxScore = function () { return 1; };
          Quiz.prototype.attach = function ($container) {
            var self = this, root = $container[0], answers = self.options.answers || [], chosen = -1;
            root.innerHTML = '';
            var question = document.createElement('p');
            question.setAttribute('data-testid', 'h5p-demo-question');
            question.textContent = self.options.question || '';
            root.appendChild(question);
            answers.forEach(function (answer, index) {
              var label = document.createElement('label'), input = document.createElement('input');
              input.type = 'radio'; input.name = 'lms-demo-answer'; input.value = String(index);
              input.setAttribute('data-testid', 'h5p-demo-answer-' + index);
              input.addEventListener('change', function () { chosen = index; });
              label.appendChild(input); label.appendChild(document.createTextNode(' ' + answer.text));
              label.style.display = 'block';
              root.appendChild(label);
            });
            var button = document.createElement('button'), result = document.createElement('p');
            button.type = 'button'; button.textContent = 'Submit';
            button.setAttribute('data-testid', 'h5p-demo-submit');
            result.setAttribute('data-testid', 'h5p-demo-result');
            button.addEventListener('click', function () {
              if (chosen < 0) { return; }
              var score = answers[chosen] && answers[chosen].correct ? 1 : 0;
              result.textContent = 'Score ' + score + ' of 1';
              button.disabled = true;
              self.triggerXAPIScored(score, 1, 'answered', true, score === 1);
            });
            root.appendChild(button); root.appendChild(result);
          };
          return Quiz;
        })();
        """);

    public static byte[] H5pQuiz() => RawZipWriter.Write(
    [
        F("h5p.json", L($$"""
            {"title":"LMS Demo Quiz","language":"en","mainLibrary":"{{QuizLibrary}}","embedTypes":["iframe"],"license":"U","preloadedDependencies":[{"machineName":"{{QuizLibrary}}","majorVersion":1,"minorVersion":0}]}
            """)),
        F("content/content.json", L("""
            {"question":"What is 2 + 2?","answers":[{"text":"3","correct":false},{"text":"4","correct":true},{"text":"5","correct":false}]}
            """)),
        F($"{QuizLibrary}-1.0/library.json", L($$"""
            {"title":"LMS Demo Quiz","machineName":"{{QuizLibrary}}","majorVersion":1,"minorVersion":0,"patchVersion":0,"runnable":1,"license":"U","author":"LMS team","coreApi":{"majorVersion":1,"minorVersion":0},"preloadedJs":[{"path":"quiz.js"}],"preloadedCss":[{"path":"quiz.css"}]}
            """)),
        F($"{QuizLibrary}-1.0/quiz.js", QuizJs()),
        F($"{QuizLibrary}-1.0/quiz.css", "[data-testid=\"h5p-demo-question\"] { font-weight: bold; }\n"),
    ]);
}
