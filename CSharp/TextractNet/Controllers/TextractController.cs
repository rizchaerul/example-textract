namespace TextractNet.Controllers;

using Microsoft.AspNetCore.Mvc;
using Amazon.Textract;
using Amazon.Textract.Model;
using System.Text.Json;

/// <summary>
/// Important resources
/// 1. Analyze Expense Request & Response: https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeExpense.html
/// 2. Analyze Document Request & Response: https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeDocument.html
/// 3. Getting credentials: https://docs.aws.amazon.com/general/latest/gr/acct-identifiers.html
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class TextractController : ControllerBase
{
    private readonly AmazonTextractClient _textractClient;
    private readonly MemoryStream _ms;

    public TextractController()
    {
        // Configure AWS first using AWS CLI, after that run 'aws configure'
        _textractClient = new();
        _ms = new();

        var file = new FileStream("SampleFiles/nhs.pdf", FileMode.Open, FileAccess.Read);
        // var file = new FileStream("SampleFiles/jb-payslip.pdf", FileMode.Open, FileAccess.Read);
        // var file = new FileStream("SampleFiles/resource-solutions-payslip.pdf", FileMode.Open, FileAccess.Read);
        file.CopyTo(_ms);
    }

    [HttpPost("expense-api")]
    public async Task<ActionResult> GetExpenseResult()
    {
        // https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeDocument.html
        var analyzeExResult = await _textractClient.AnalyzeExpenseAsync(new AnalyzeExpenseRequest
        {
            Document = new Document
            {
                Bytes = _ms
            },
        });

        var res = analyzeExResult.ExpenseDocuments
            .Select(e => new
            {
                Summaries = e.SummaryFields
                    .Select(s =>
                    {
                        var key = s.LabelDetection?.Text;

                        if (key == null)
                        {
                            key = s.Type.Text;
                        }

                        var value = s.ValueDetection.Text;

                        return new { Key = key, Value = value, };
                    }),

                Tables = e.LineItemGroups.Select(li => li.LineItems.Select(l => new
                {
                    Rows = l.LineItemExpenseFields
                        .Where(lef => lef.Type.Text != "EXPENSE_ROW")
                        .Select(lef => new
                        {
                            ColumnName = lef.LabelDetection.Text,
                            Value = lef.ValueDetection.Text,
                        })
                }))
            });

        return Ok(res);
    }

    [HttpGet("document-api")]
    public async Task<ActionResult> GetDocumentResult()
    {
        // https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeDocument.html
        var analyzeDocResult = await _textractClient.AnalyzeDocumentAsync(new AnalyzeDocumentRequest
        {
            Document = new Document
            {
                Bytes = _ms
            },

            FeatureTypes = new List<string>
            {
                "TABLES", "FORMS", "QUERIES"
            },

            QueriesConfig = new QueriesConfig
            {
                Queries = new List<Query>
                {
                    new Query
                    {
                        Text = "Who's the employee?"
                    }
                }
            }
        });

        var blockDict = analyzeDocResult.Blocks.ToDictionary(b => b.Id);

        return Ok(new
        {
            Queries = analyzeDocResult.Blocks
                .Where(b => b.BlockType == BlockType.QUERY)
                .Select(b => new
                {
                    Question = b.Query.Text,
                    Answer = analyzeDocResult.Blocks
                        .Where(bl =>
                        {
                            var ids = b.Relationships.Where(r => r.Type == RelationshipType.ANSWER).FirstOrDefault()?.Ids;

                            if (ids != null)
                            {
                                return ids.Contains(bl.Id);
                            }

                            return false;
                        })
                        .FirstOrDefault()?.Text
                })
                .FirstOrDefault(),

            Form = analyzeDocResult.Blocks
                .Where(b => b.BlockType == BlockType.KEY_VALUE_SET && b.EntityTypes.Contains("KEY"))
                .Select(b =>
                {
                    var key = CreateText(b, analyzeDocResult);

                    var valueIds = b.Relationships
                        .Where(r => r.Type == RelationshipType.VALUE)
                        .FirstOrDefault()?.Ids;
                    var valueBlock = analyzeDocResult.Blocks
                        .Where(b => valueIds != null && valueIds.Contains(b.Id))
                        .FirstOrDefault();

                    string? value = null;

                    if (valueBlock != null)
                    {
                        value = CreateText(valueBlock, analyzeDocResult);
                    }

                    return new
                    {
                        Key = key,
                        Value = value,
                    };
                }),

            Tables = analyzeDocResult.Blocks
                .Where(b => b.BlockType == BlockType.TABLE)
                .Select(b =>
                {
                    var ids = b.Relationships
                        .Where(r => r.Type == RelationshipType.CHILD)
                        .Select(r => r.Ids)
                        .FirstOrDefault();

                    var cellBlocks = analyzeDocResult.Blocks
                        .Where(b => ids != null && ids.Contains(b.Id))
                        .OrderBy(b => b.RowIndex)
                        .ThenBy(b => b.ColumnIndex)
                        .GroupBy(b => b.RowIndex);

                    var rows = cellBlocks
                        .Select(b => b.Select(r => CreateText(r, analyzeDocResult)));

                    return new
                    {
                        Rows = rows
                    };
                })
        });
    }

    private static string CreateText(Block block, AnalyzeDocumentResponse analyzeDocumentResponse)
    {
        var keyIds = block.Relationships
            .Find(r => r.Type == RelationshipType.CHILD)?.Ids;

        var wordBlocks = analyzeDocumentResponse.Blocks
            .Where(b => keyIds != null && keyIds.Contains(b.Id));

        var words = wordBlocks
            .Select(w =>
            {
                if (w.BlockType == BlockType.SELECTION_ELEMENT)
                {
                    return w.SelectionStatus == BlockType.SELECTION_ELEMENT ? "true" : "false";
                }
                else if (w.BlockType == BlockType.WORD)
                {
                    return w.Text;
                }

                return null;
            });

        return string.Join(" ", words);
    }
}
