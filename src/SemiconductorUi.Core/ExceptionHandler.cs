using System;
using System.IO;

namespace SemiconductorUi
{
    /// <summary>
    /// 애플리케이션 전역 예외 처리 및 로깅 유틸리티
    /// </summary>
    public static class ExceptionHandler
    {
        /// <summary>
        /// 사용자에게 오류 메시지를 표시하는 방법 (표현 계층에서 설정, 예: WPF MessageBox)
        /// </summary>
        public static Action<string> ShowErrorMessage { get; set; }

        /// <summary>
        /// 예외를 처리하고 로그를 기록
        /// </summary>
        /// <param name="ex">발생한 예외</param>
        /// <param name="context">예외 발생 컨텍스트 (예: "파일 로드", "하드웨어 제어")</param>
        /// <param name="logCallback">로그 콜백 함수 (선택사항)</param>
        /// <returns>처리된 예외 메시지</returns>
        public static string HandleException(Exception ex, string context = "", Action<string, string> logCallback = null)
        {
            if (ex == null)
            {
                return "알 수 없는 오류가 발생했습니다.";
            }

            string errorMessage = FormatExceptionMessage(ex, context);
            string logMessage = $"[{context}] {errorMessage}";

            // 로그 콜백이 있으면 사용, 없으면 기본 로깅
            if (logCallback != null)
            {
                logCallback(logMessage, "ERROR");
            }
            else
            {
                LogToFile(logMessage);
            }

            return errorMessage;
        }

        /// <summary>
        /// 예외 메시지를 사용자 친화적인 형식으로 포맷
        /// </summary>
        private static string FormatExceptionMessage(Exception ex, string context)
        {
            string message = ex.Message;

            // 특정 예외 타입에 대한 사용자 친화적인 메시지
            if (ex is FileNotFoundException)
            {
                message = $"파일을 찾을 수 없습니다: {ex.Message}";
            }
            else if (ex is UnauthorizedAccessException)
            {
                message = $"파일 접근 권한이 없습니다: {ex.Message}";
            }
            else if (ex is IOException)
            {
                message = $"파일 입출력 오류가 발생했습니다: {ex.Message}";
            }
            else if (ex is ArgumentException)
            {
                message = $"잘못된 인수가 전달되었습니다: {ex.Message}";
            }
            else if (ex is ArgumentNullException)
            {
                message = $"필수 인수가 제공되지 않았습니다: {ex.Message}";
            }
            else if (ex is InvalidOperationException)
            {
                message = $"잘못된 작업이 수행되었습니다: {ex.Message}";
            }
            else if (ex is TimeoutException)
            {
                message = $"작업 시간이 초과되었습니다: {ex.Message}";
            }

            // 컨텍스트가 있으면 추가
            if (!string.IsNullOrEmpty(context))
            {
                message = $"{context}: {message}";
            }

            return message;
        }

        /// <summary>
        /// 예외를 파일에 로그 기록
        /// </summary>
        private static void LogToFile(string message)
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string logFile = Path.Combine(logDirectory, $"Error_{DateTime.Now:yyyyMMdd}.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                File.AppendAllText(logFile, logEntry);
            }
            catch
            {
                // 로그 기록 실패 시 무시 (무한 루프 방지)
            }
        }

        /// <summary>
        /// 예외를 안전하게 처리하고 기본값 반환
        /// </summary>
        /// <typeparam name="T">반환 타입</typeparam>
        /// <param name="action">실행할 작업</param>
        /// <param name="defaultValue">예외 발생 시 반환할 기본값</param>
        /// <param name="context">예외 발생 컨텍스트</param>
        /// <param name="logCallback">로그 콜백 함수</param>
        /// <returns>작업 결과 또는 기본값</returns>
        public static T SafeExecute<T>(Func<T> action, T defaultValue = default(T), string context = "", Action<string, string> logCallback = null)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
        }

        /// <summary>
        /// 예외를 안전하게 처리하고 void 작업 실행
        /// </summary>
        /// <param name="action">실행할 작업</param>
        /// <param name="context">예외 발생 컨텍스트</param>
        /// <param name="logCallback">로그 콜백 함수</param>
        /// <param name="showMessageBox">사용자에게 메시지 박스 표시 여부</param>
        public static void SafeExecute(Action action, string context = "", Action<string, string> logCallback = null, bool showMessageBox = false)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                string errorMessage = HandleException(ex, context, logCallback);
                
                if (showMessageBox)
                {
                    ShowErrorMessage?.Invoke(errorMessage);
                }
            }
        }

        /// <summary>
        /// 파일 작업을 안전하게 실행 (void 반환)
        /// </summary>
        /// <param name="fileAction">파일 작업</param>
        /// <param name="fileName">파일명 (로깅용)</param>
        /// <param name="logCallback">로그 콜백 함수</param>
        public static void SafeFileOperation(Action fileAction, string fileName = "", Action<string, string> logCallback = null)
        {
            SafeExecute(fileAction, string.IsNullOrEmpty(fileName) ? "파일 작업" : $"파일 작업 ({fileName})", logCallback);
        }

        /// <summary>
        /// 파일 작업을 안전하게 실행
        /// </summary>
        /// <typeparam name="T">반환 타입</typeparam>
        /// <param name="fileAction">파일 작업</param>
        /// <param name="defaultValue">예외 발생 시 반환할 기본값</param>
        /// <param name="fileName">파일명 (로깅용)</param>
        /// <param name="logCallback">로그 콜백 함수</param>
        /// <returns>작업 결과 또는 기본값</returns>
        public static T SafeFileOperation<T>(Func<T> fileAction, T defaultValue = default(T), string fileName = "", Action<string, string> logCallback = null)
        {
            try
            {
                return fileAction();
            }
            catch (FileNotFoundException ex)
            {
                string context = string.IsNullOrEmpty(fileName) ? "파일 작업" : $"파일 작업 ({fileName})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                string context = string.IsNullOrEmpty(fileName) ? "파일 접근" : $"파일 접근 ({fileName})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
            catch (IOException ex)
            {
                string context = string.IsNullOrEmpty(fileName) ? "파일 입출력" : $"파일 입출력 ({fileName})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
            catch (Exception ex)
            {
                string context = string.IsNullOrEmpty(fileName) ? "파일 작업" : $"파일 작업 ({fileName})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
        }

        /// <summary>
        /// 하드웨어 작업을 안전하게 실행
        /// </summary>
        /// <typeparam name="T">반환 타입</typeparam>
        /// <param name="hardwareAction">하드웨어 작업</param>
        /// <param name="defaultValue">예외 발생 시 반환할 기본값</param>
        /// <param name="operation">작업명 (로깅용)</param>
        /// <param name="logCallback">로그 콜백 함수</param>
        /// <returns>작업 결과 또는 기본값</returns>
        public static T SafeHardwareOperation<T>(Func<T> hardwareAction, T defaultValue = default(T), string operation = "", Action<string, string> logCallback = null)
        {
            try
            {
                return hardwareAction();
            }
            catch (TimeoutException ex)
            {
                string context = string.IsNullOrEmpty(operation) ? "하드웨어 작업" : $"하드웨어 작업 ({operation})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
            catch (InvalidOperationException ex)
            {
                string context = string.IsNullOrEmpty(operation) ? "하드웨어 상태 오류" : $"하드웨어 상태 오류 ({operation})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
            catch (Exception ex)
            {
                string context = string.IsNullOrEmpty(operation) ? "하드웨어 작업" : $"하드웨어 작업 ({operation})";
                HandleException(ex, context, logCallback);
                return defaultValue;
            }
        }
    }
}

