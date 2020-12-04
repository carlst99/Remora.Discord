//
//  UpdateableRecordGenerator.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Remora.Discord.Generators.Extensions;
using Remora.Discord.Generators.Support;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Remora.Discord.Generators
{
    /// <summary>
    /// Generates supporting code for updateable records. Updateable records have an Update(TRecord) member on them that
    /// automatically copy new or changed properties from the given record to a new instance.
    /// </summary>
    [Generator]
    public class UpdateableRecordGenerator : ISourceGenerator
    {
        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new UpdateableRecordSyntaxReceiver());
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not UpdateableRecordSyntaxReceiver syntaxReceiver)
            {
                throw new InvalidOperationException();
            }

            foreach (var diagnostic in syntaxReceiver.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            foreach (var updateableRecord in syntaxReceiver.UpdatableRecords)
            {
                AddUpdateableRecordSource(context, updateableRecord);
            }
        }

        /// <summary>
        /// Adds generated support code for the given updateable record.
        /// </summary>
        /// <param name="context">The generation context.</param>
        /// <param name="updateableRecord">The updateable record.</param>
        private static void AddUpdateableRecordSource
        (
            GeneratorExecutionContext context,
            RecordDeclarationSyntax updateableRecord
        )
        {
            var syntaxTree = updateableRecord.SyntaxTree;
            var model = context.Compilation.GetSemanticModel(syntaxTree);

            var declaredSymbol = model.GetDeclaredSymbol(updateableRecord);
            var containingNamespace = declaredSymbol?.ContainingNamespace;
            if (containingNamespace is null)
            {
                return;
            }

            var generatedPart = CompilationUnit()
                .AddMembers
                (
                    NamespaceDeclaration(IdentifierName(containingNamespace.Name))
                    .AddMembers
                    (
                        CreatePartialRecordDeclaration(updateableRecord)
                    )
                )
                .WithLeadingTrivia(Comment("// <auto-generated>"))
                .NormalizeWhitespace();

            var text = generatedPart.GetText(Encoding.UTF8);
            context.AddSource($"{updateableRecord.Identifier.ToString()}.g.cs", text);
        }

        /// <summary>
        /// Creates the partial record declaration where the Update(TRecord) method is placed.
        /// </summary>
        /// <param name="updateableRecord">The updateable record.</param>
        /// <returns>The record declaration.</returns>
        private static RecordDeclarationSyntax CreatePartialRecordDeclaration(RecordDeclarationSyntax updateableRecord)
        {
            return RecordDeclaration
            (
                Token(RecordKeyword),
                updateableRecord.Identifier
            )
            .AddModifiers(updateableRecord.Modifiers.ToArray())
            .WithOpenBraceToken(Token(OpenBraceToken))
            .WithCloseBraceToken(Token(CloseBraceToken))
            .AddMembers
            (
                CreateUpdateMethodDeclaration(updateableRecord)
            )
            .AddBaseListTypes
            (
                SimpleBaseType
                (
                    GenericName
                    (
                        typeof(IUpdateable<>).GetIdentifierName()
                    )
                    .AddTypeArgumentListArguments
                    (
                        IdentifierName(updateableRecord.Identifier)
                    )
                )
            );
        }

        /// <summary>
        /// Creates the Update(TRecord) method.
        /// </summary>
        /// <param name="updateableRecord">The updateable record.</param>
        /// <returns>The method.</returns>
        private static MethodDeclarationSyntax CreateUpdateMethodDeclaration(RecordDeclarationSyntax updateableRecord)
        {
            var memberUpdateExpressions = CreateMemberUpdateExpressions(updateableRecord);

            return MethodDeclaration
            (
                ParseTypeName(updateableRecord.Identifier.Text),
                "Update"
            )
            .AddModifiers(Token(PublicKeyword))
            .AddParameterListParameters
            (
                Parameter(Identifier("other"))
                .WithType(ParseTypeName(updateableRecord.Identifier.Text))
            )
            .WithBody
            (
                Block()
                .AddStatements
                (
                    ReturnStatement
                    (
                        WithExpression
                        (
                            ThisExpression(),
                            InitializerExpression
                            (
                                WithInitializerExpression,
                                SeparatedList<ExpressionSyntax>()
                            )
                        )
                        .AddInitializerExpressions(memberUpdateExpressions.ToArray())
                    )
                )
            );
        }

        /// <summary>
        /// Creates a set of expressions that update each individual member of the record.
        /// </summary>
        /// <param name="updateableRecord">The updateable record.</param>
        /// <returns>The expressions.</returns>
        private static List<ExpressionSyntax> CreateMemberUpdateExpressions(RecordDeclarationSyntax updateableRecord)
        {
            var withAssignments = new List<ExpressionSyntax>();
            if (updateableRecord.ParameterList is null)
            {
                return withAssignments;
            }

            foreach (var recordMember in updateableRecord.ParameterList.Parameters)
            {
                ExpressionSyntax expressionSyntax;
                if (recordMember.IsOptional())
                {
                    expressionSyntax = CreateOptionalUpdateExpression(recordMember);
                }
                else if (recordMember.IsNullableOptional())
                {
                    expressionSyntax = CreateNullableOptionalUpdateExpression(recordMember);
                }
                else
                {
                    expressionSyntax = CreateSimpleUpdateExpression(recordMember);
                }

                withAssignments.Add(expressionSyntax);
            }

            return withAssignments;
        }

        /// <summary>
        /// Creates a direct assignment expression.
        /// </summary>
        /// <param name="recordMember">The parameter-style declaration of the member to be updated.</param>
        /// <returns>A fully formed assignment expression.</returns>
        private static AssignmentExpressionSyntax CreateSimpleUpdateExpression(ParameterSyntax recordMember)
        {
            return AssignmentExpression
            (
                SimpleAssignmentExpression,
                IdentifierName(recordMember.Identifier.Text),
                MemberAccessExpression
                (
                    SimpleMemberAccessExpression,
                    IdentifierName("other"),
                    IdentifierName(recordMember.Identifier.Text)
                )
            );
        }

        /// <summary>
        /// Creates an expression that selects the original optional or the new optional, based on whether the new
        /// optional has a value present. If the new value is null, that is the copied value.
        /// </summary>
        /// <param name="recordMember">The parameter-style declaration of the member to be updated.</param>
        /// <returns>A fully formed assignment expression.</returns>
        private static AssignmentExpressionSyntax CreateNullableOptionalUpdateExpression(ParameterSyntax recordMember)
        {
            return AssignmentExpression
            (
                SimpleAssignmentExpression,
                IdentifierName(recordMember.Identifier.Text),
                ConditionalExpression
                (
                    IsPatternExpression
                    (
                        MemberAccessExpression
                        (
                            SimpleMemberAccessExpression,
                            IdentifierName("other"),
                            IdentifierName(recordMember.Identifier.Text)
                        ),
                        ConstantPattern(LiteralExpression(NullLiteralExpression))
                    ),
                    MemberAccessExpression
                    (
                        SimpleMemberAccessExpression,
                        IdentifierName("other"),
                        IdentifierName(recordMember.Identifier.Text)
                    ),
                    ConditionalExpression
                    (
                        MemberAccessExpression
                        (
                            SimpleMemberAccessExpression,
                            MemberAccessExpression
                            (
                                SimpleMemberAccessExpression,
                                IdentifierName("other"),
                                IdentifierName(recordMember.Identifier.Text)
                            ),
                            IdentifierName("HasValue")
                        ),
                        MemberAccessExpression
                        (
                            SimpleMemberAccessExpression,
                            IdentifierName("other"),
                            IdentifierName(recordMember.Identifier.Text)
                        ),
                        MemberAccessExpression
                        (
                            SimpleMemberAccessExpression,
                            ThisExpression(),
                            IdentifierName(recordMember.Identifier.Text)
                        )
                    )
                )
            );
        }

        /// <summary>
        /// Creates an expression that selects the original optional or the new optional, based on whether the new
        /// optional has a value present.
        /// </summary>
        /// <param name="recordMember">The parameter-style declaration of the member to be updated.</param>
        /// <returns>A fully formed assignment expression.</returns>
        private static AssignmentExpressionSyntax CreateOptionalUpdateExpression(ParameterSyntax recordMember)
        {
            return AssignmentExpression
            (
                SimpleAssignmentExpression,
                IdentifierName(recordMember.Identifier.Text),
                ConditionalExpression
                (
                    MemberAccessExpression
                    (
                        SimpleMemberAccessExpression,
                        MemberAccessExpression
                        (
                            SimpleMemberAccessExpression,
                            IdentifierName("other"),
                            IdentifierName(recordMember.Identifier.Text)
                        ),
                        IdentifierName("HasValue")
                    ),
                    MemberAccessExpression
                    (
                        SimpleMemberAccessExpression,
                        IdentifierName("other"),
                        IdentifierName(recordMember.Identifier.Text)
                    ),
                    MemberAccessExpression
                    (
                        SimpleMemberAccessExpression,
                        ThisExpression(),
                        IdentifierName(recordMember.Identifier.Text)
                    )
                )
            );
        }
    }
}
