import {
    AnalyzeDocumentCommand,
    AnalyzeExpenseCommand,
    TextractClient,
} from "@aws-sdk/client-textract";
import { readFile, writeFile } from "fs/promises";

(async () => {
    const file = await readFile("./samples/nhs.pdf");
    // const file = await readFile("./samples/resource-solutions-payslip.pdf");
    // const file = await readFile("./samples/jb-payslip.pdf");

    const client = new TextractClient({
        region: "us-west-2",
    });

    const docCommand = new AnalyzeDocumentCommand({
        Document: { Bytes: file },
        // See https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeDocument.html
        FeatureTypes: ["TABLES", "FORMS", "QUERIES"],
        QueriesConfig: {
            Queries: [
                {
                    Text: "Who's the employee?",
                },
            ],
        },
    });

    const expenseCommand = new AnalyzeExpenseCommand({
        // See https://docs.aws.amazon.com/textract/latest/dg/API_AnalyzeExpense.html
        Document: { Bytes: file },
    });

    let AnalyzeDocOutput = await client.send(docCommand);
    await writeFile(
        "./results/document-api/raw.json",
        JSON.stringify(AnalyzeDocOutput, null, 2)
    );

    // Delete geometry from object
    AnalyzeDocOutput.Blocks?.forEach((b) => {
        delete b.Geometry;
    });
    await writeFile(
        "./results/document-api/raw-no-geometry.json",
        JSON.stringify(AnalyzeDocOutput, null, 2)
    );

    let expenseCommandResult = await client.send(expenseCommand);
    await writeFile(
        "./results/expense-api/raw.json",
        JSON.stringify(expenseCommandResult, null, 2)
    );

    // Delete geometry from object
    expenseCommandResult.ExpenseDocuments?.forEach((e) => {
        e.SummaryFields?.forEach((s) => {
            delete s.LabelDetection?.Geometry;
            delete s.ValueDetection?.Geometry;
        });

        e.LineItemGroups?.forEach((l) =>
            l.LineItems?.forEach((l) =>
                l.LineItemExpenseFields?.forEach((l) => {
                    delete l.LabelDetection?.Geometry;
                    delete l.ValueDetection?.Geometry;
                })
            )
        );
    });

    await writeFile(
        "./results/expense-api/raw-no-geometry.json",
        JSON.stringify(expenseCommandResult, null, 2)
    );
})();
