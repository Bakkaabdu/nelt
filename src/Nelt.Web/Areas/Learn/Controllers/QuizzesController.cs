using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Quizzes;

namespace Nelt.Web.Areas.Learn.Controllers;

[Route("learn/courses/{courseId:int}/quizzes")]
public sealed class QuizzesController(IQuizTakingService quizzes) : LearnController
{
    [HttpGet("{quizId:int}")]
    public async Task<IActionResult> Show(int courseId, int quizId)
    {
        var result = await quizzes.IntroAsync(courseId, quizId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{quizId:int}/start")]
    public async Task<IActionResult> Start(int courseId, int quizId)
    {
        var result = await quizzes.StartAsync(courseId, quizId, Aborted);
        if (result.Failed)
        {
            return RedirectWithResult(result, string.Empty, nameof(Show), new { courseId, quizId });
        }

        return RedirectToAction(nameof(Take), new { courseId, attemptId = result.Value });
    }

    [HttpGet("attempts/{attemptId:int}")]
    public async Task<IActionResult> Take(int courseId, int attemptId)
    {
        var result = await quizzes.SheetAsync(courseId, attemptId, Aborted);
        if (result.Failed)
        {
            if (result.Error!.Kind == Application.Common.ErrorKind.Conflict)
            {
                Flash(result.Error.Message, Infrastructure.Mvc.FlashKind.Info);
                return RedirectToAction(nameof(Review), new { courseId, attemptId });
            }

            return Failure(result.Error);
        }

        return View(result.Value);
    }

    [HttpPost("attempts/{attemptId:int}")]
    public async Task<IActionResult> Submit(int courseId, int attemptId, IFormCollection form)
    {
        // Answers arrive as q{questionId}=optionId (radio) or several q{questionId}=optionId (checkboxes).
        var answers = new Dictionary<int, int[]>();
        foreach (var (key, values) in form)
        {
            if (key.Length > 1 && key[0] == 'q' && int.TryParse(key.AsSpan(1), out var questionId))
            {
                answers[questionId] = values.Select(v => int.TryParse(v, out var id) ? id : 0).Where(id => id > 0).Take(20).ToArray();
            }
        }

        var result = await quizzes.SubmitAsync(courseId, attemptId, answers, Aborted);
        return result.Failed ? Failure(result.Error!) : RedirectToAction(nameof(Review), new { courseId, attemptId });
    }

    [HttpGet("attempts/{attemptId:int}/result")]
    public async Task<IActionResult> Review(int courseId, int attemptId)
    {
        var result = await quizzes.ReviewAsync(courseId, attemptId, Aborted);
        if (result.Failed && result.Error!.Kind == Application.Common.ErrorKind.Conflict)
        {
            return RedirectToAction(nameof(Take), new { courseId, attemptId });
        }

        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }
}
