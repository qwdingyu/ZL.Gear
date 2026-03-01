namespace ZL.Gear.Engine.Evaluation
{
    public class EvaluationResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        private EvaluationResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static EvaluationResult Pass(string message) => new EvaluationResult(true, message);
        public static EvaluationResult Fail(string message) => new EvaluationResult(false, message);
    }

}