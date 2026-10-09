using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Quizzes;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

[Route("teach/courses/{courseId:int}/quizzes")]
public sealed class QuizzesController(IQuizAuthoringService quizzes) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await quizzes.ListAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("new")]
    public Task<IActionResult> Create(int courseId) => FormAsync(courseId, null, null);

    [HttpPost("new")]
    public async Task<IActionResult> Create(int courseId, [Bind(Prefix = "Input")] QuizInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await quizzes.CreateAsync(courseId, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The quiz was created. Now add its questions.");
                return RedirectToAction(nameof(Questions), new { courseId, quizId = result.Value });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, null, input);
    }

    [HttpGet("{quizId:int}")]
    public Task<IActionResult> Edit(int courseId, int quizId) => FormAsync(courseId, quizId, null);

    [HttpPost("{quizId:int}")]
    public async Task<IActionResult> Edit(int courseId, int quizId, [Bind(Prefix = "Input")] QuizInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await quizzes.UpdateAsync(courseId, quizId, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The quiz was saved.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, quizId, input);
    }

    [HttpPost("{quizId:int}/delete")]
    public async Task<IActionResult> Delete(int courseId, int quizId)
        => RedirectWithResult(await quizzes.DeleteAsync(courseId, quizId, Aborted), "The quiz was deleted.", nameof(Index), new { courseId });

    [HttpGet("{quizId:int}/questions")]
    public async Task<IActionResult> Questions(int courseId, int quizId)
    {
        var result = await quizzes.QuestionsAsync(courseId, quizId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{quizId:int}/questions")]
    public async Task<IActionResult> SaveQuestion(int courseId, int quizId, [Bind(Prefix = "Question")] QuestionInput input)
    {
        if (!ModelState.IsValid)
        {
            var error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m));
            TempData[FlashMessages.MessageKey] = error ?? L["Please check the question."].Value;
            TempData[FlashMessages.KindKey] = "error";
            return RedirectToAction(nameof(Questions), new { courseId, quizId });
        }

        var result = await quizzes.SaveQuestionAsync(courseId, quizId, input, Aborted);
        return RedirectWithResult(result, "The question was saved.", nameof(Questions), new { courseId, quizId });
    }

    [HttpPost("{quizId:int}/questions/{questionId:int}/delete")]
    public async Task<IActionResult> DeleteQuestion(int courseId, int quizId, int questionId)
        => RedirectWithResult(await quizzes.DeleteQuestionAsync(courseId, quizId, questionId, Aborted), "The question was deleted.", nameof(Questions), new { courseId, quizId });

    [HttpGet("{quizId:int}/results")]
    public async Task<IActionResult> Results(int courseId, int quizId)
    {
        var result = await quizzes.ResultsAsync(courseId, quizId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{quizId:int}/results")]
    public async Task<IActionResult> RecordScore(int courseId, int quizId, RecordScoreInput input)
    {
        if (!ModelState.IsValid)
        {
            Flash("Please enter a score between 0 and 100.", FlashKind.Error);
            return RedirectToAction(nameof(Results), new { courseId, quizId });
        }

        return RedirectWithResult(await quizzes.RecordScoreAsync(courseId, quizId, input, Aborted), "The score was recorded.", nameof(Results), new { courseId, quizId });
    }

    private async Task<IActionResult> FormAsync(int courseId, int? quizId, QuizInput? posted)
    {
        var result = await quizzes.GetForEditAsync(courseId, quizId, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return View("Form", posted is null ? result.Value : result.Value with { Input = posted });
    }
}
