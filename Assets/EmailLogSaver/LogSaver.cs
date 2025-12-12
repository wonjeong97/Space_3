/*==============================================================================================================================
* Gmail 설정 방법 (필수)
* Gmail을 보내는 메일로 사용하려면 다음 설정이 필요합니다.
* * 1. 구글 계정 관리 > 보안 탭으로 이동.
* * 2. 2단계 인증이 켜져 있어야 합니다.
* * 3. 2단계 인증 설정 하단에 [앱 비밀번호] 항목을 찾아 클릭합니다. (검색창에 '앱 비밀번호' 검색 가능)
* * 4. 앱 이름에 'UnityLog' 등으로 입력하고 생성하기를 누릅니다.
* * 생성된 16자리 비밀번호를 복사해서 위 코드의 senderPassword 변수에 붙여넣으세요. (기존 구글 로그인 비번은 작동하지 않습니다)
*==============================================================================================================================*/

using UnityEngine;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

public enum LogTriggerLevel
{
    Everything,      // 모든 로그
    WarningOrAbove,  // 경고, 에러, 예외
    ErrorOrAbove     // 에러, 예외 (기본값)
}

/// <summary>
/// 런타임 중 발생하는 로그를 수집하여 파일로 저장하고, 
/// 특정 조건(에러 등) 충족 시 지정된 이메일로 로그 파일을 전송하는 클래스입니다.
/// </summary>
public class LogSaver : MonoBehaviour
{
    // 싱글톤 패턴 적용
    public static LogSaver Instance { get; private set; }

    [Header("Save Settings (PC)")]
    [Tooltip("체크하면 아래 Custom Path를 사용합니다. 해제하면 실행 파일(또는 프로젝트) 폴더에 저장합니다.")]
    [SerializeField] private bool useCustomPath = false;

    [Tooltip("로그를 저장할 절대 경로입니다. (예: C:/Logs)")]
    [SerializeField] private string customPath = "C:/Logs";

    [Header("General Settings")]
    [Tooltip("체크하면 조건 충족 시 메일을 보냅니다. (해제 시 파일 저장만 수행)")]
    [SerializeField] private bool enableEmail = true;

    [Tooltip("어떤 로그가 발생했을 때 메일을 보낼지 설정합니다.")]
    [SerializeField] private LogTriggerLevel triggerLevel = LogTriggerLevel.ErrorOrAbove;

    [Header("Email Settings")]
    [SerializeField] private string senderEmail = "your_email@gmail.com";
    [SerializeField] private string senderPassword = "your_app_password";
    [SerializeField] private string recipientEmail = "target_email@example.com";
    [SerializeField] private string smtpServer = "smtp.gmail.com";
    [SerializeField] private int smtpPort = 587;

    private readonly StringBuilder _logBuffer = new StringBuilder();
    private string _currentLogPath;
    private string _logFolder;
    
    // 메일 발송 조건 충족 여부
    private bool _shouldSendEmail; 

    private void Awake()
    {
        // 싱글톤 & DontDestroyOnLoad 설정
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // PC 전용 경로 설정 로직
        if (useCustomPath && !string.IsNullOrEmpty(customPath))
        {
            _logFolder = customPath;
        }
        else
        {
            // Application.dataPath:
            //   - 에디터: <Project>/Assets
            //   - 빌드: <Build>/<AppName>_Data
            // Path.GetDirectoryName(...)를 사용해 상위 폴더(실행 위치)를 가져옵니다.
            string basePath = Path.GetDirectoryName(Application.dataPath);
            _logFolder = Path.Combine(basePath, "Logs");
        }

        // 폴더가 없으면 생성
        try
        {
            if (!Directory.Exists(_logFolder)) Directory.CreateDirectory(_logFolder);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LogSaver] 로그 폴더 생성 실패({_logFolder}): {e.Message}");
            return;
        }
        
        string fileName = $"Log_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        _currentLogPath = Path.Combine(_logFolder, fileName);

        Debug.Log($"[LogSaver] 로그 저장 경로: {_currentLogPath}");

        Application.logMessageReceived += HandleLog;
    }

    private void Start()
    {
        // 시작 시 이전에 전송 실패한 로그가 있다면 재전송 시도
        if (enableEmail)
        {
            TrySendPendingLogsAsync().ConfigureAwait(false);
        }
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    // 로그 발생 시 호출되는 콜백
    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        // 1. 발송 조건 체크
        bool isTrigger = false;
        switch (triggerLevel)
        {
            case LogTriggerLevel.Everything:
                isTrigger = true; 
                break;
            
            case LogTriggerLevel.WarningOrAbove:
                if (type != LogType.Log) isTrigger = true;
                break;
            
            case LogTriggerLevel.ErrorOrAbove:
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    isTrigger = true;
                break;
        }

        if (isTrigger)
        {
            _shouldSendEmail = true;
        }

        // 2. 로그 기록
        _logBuffer.AppendLine($"[{System.DateTime.Now:HH:mm:ss}] [{type}] {logString}");
        
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            _logBuffer.AppendLine($"Stack Trace: {stackTrace}");
        }
    }

    // 어플리케이션 종료 시 호출
    private void OnApplicationQuit()
    {
        // 조건이 충족되었을 때만 저장
        if (_shouldSendEmail)
        {
            SaveLogToFile();

            // 메일 발송 옵션이 켜져있을 때만 전송 시도
            if (enableEmail)
            {
                TrySendSingleLog(_currentLogPath); 
            }
        }
    }

    private void SaveLogToFile()
    {
        if (_logBuffer.Length > 0)
        {
            try 
            {
                File.WriteAllText(_currentLogPath, _logBuffer.ToString());
                Debug.Log($"[LogSaver] 로그 파일 저장 완료: {_currentLogPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LogSaver] 파일 저장 실패: {e.Message}");
            }
        }
    }

    // 미전송 로그 일괄 발송 (비동기)
    private async Task TrySendPendingLogsAsync()
    {
        if (!Directory.Exists(_logFolder)) return;

        string[] files = Directory.GetFiles(_logFolder, "*.txt");
        if (files.Length == 0) return;

        Debug.Log($"[LogSaver] 미전송 로그 {files.Length}개 발견. 재전송 시도...");

        foreach (string filePath in files)
        {
            if (filePath == _currentLogPath) continue;

            bool success = await Task.Run(() => TrySendSingleLog(filePath));
            
            // 하나라도 실패하면(인터넷 끊김 등) 중단
            if (!success) break; 
        }
    }

    // 단일 로그 파일 전송 및 삭제
    private bool TrySendSingleLog(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress(senderEmail);
                mail.To.Add(recipientEmail);
                mail.Subject = $"[{Application.productName}] Unity Log Report"; 
                mail.Body = $"발송 조건: {triggerLevel}\n로그 파일을 첨부합니다.\n파일명: {Path.GetFileName(filePath)}";

                // Attachment를 별도의 using 블록으로 감싸서 사용 후 즉시 해제 보장
                using (Attachment attachment = new Attachment(filePath))
                {
                    mail.Attachments.Add(attachment);

                    using (SmtpClient smtpClient = new SmtpClient(smtpServer))
                    {
                        smtpClient.Port = smtpPort;
                        smtpClient.Credentials = new NetworkCredential(senderEmail, senderPassword) as ICredentialsByHost;
                        smtpClient.EnableSsl = true;
                        smtpClient.Timeout = 5000;

                        ServicePointManager.ServerCertificateValidationCallback = 
                            delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) 
                            { return true; };

                        smtpClient.Send(mail);
                    }
                } 
                // 여기서 attachment.Dispose()가 호출되어 파일 잠금이 확실하게 풀림
            }

            Debug.Log($"[LogSaver] 전송 성공. 파일 삭제 시도: {filePath}");
            
            // 전송 성공 시 파일 삭제 (메모리 관리)
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.Log("[LogSaver] 파일 삭제 완료.");
            }
            
            return true;
        }
        catch (System.Exception e)
        {
            // 삭제 실패 원인 확인용 로그
            Debug.LogError($"[LogSaver] 처리 중 오류 발생 (전송은 성공했을 수 있음): {e.Message}");
            return false;
        }
    }
}