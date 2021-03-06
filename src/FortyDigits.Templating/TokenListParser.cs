﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace FortyDigits.Templating
{
    public class TokenListParser : ITemplateParser<TokenListTemplate>
    {
        private readonly string[] _tokens;
        protected static readonly Type DictionaryType;
        protected static readonly MethodInfo DictionaryGetMethod;
        protected static readonly MethodInfo StringJoinMethod;

        /// <summary>
        /// 
        /// </summary>
        static TokenListParser()
        {
            DictionaryType = typeof(Dictionary<string, string>);
            DictionaryGetMethod = DictionaryType.GetMethod("get_Item");
            StringJoinMethod = typeof (string).GetMethod("Join", new[] {typeof (string), typeof (string[])});
        }

        public TokenListParser(IEnumerable<string> tokens)
        {
            if (tokens == null)
                throw new ArgumentNullException(nameof(tokens));

            var tokensArray = tokens as string[] ?? tokens.ToArray();

            if (!tokensArray.Any())
                throw new ArgumentException("Tokens may not be empty", nameof(tokens));

            _tokens = tokensArray;
        }

        public TokenListTemplate GetTemplate(string templateString)
        {
            var renderMethod = GetRenderMethod(templateString);
            var template = new TokenListTemplate(renderMethod);

            return template;
        }

        protected virtual IEnumerable<KeyValuePair<int, string>> GetTokenIndices(string template, IEnumerable<string> tokens)
        {
            foreach (var token in tokens)
            {
                for (var index = 0; ; index += token.Length)
                {
                    index = template.IndexOf(token, index, StringComparison.Ordinal);
                    if (index == -1)
                        break;

                    yield return new KeyValuePair<int, string>(index, token);
                }
            }
        }

        protected virtual void CreateStringPart(ILGenerator il, int index, string stringPart)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_S, index);
            il.Emit(OpCodes.Ldstr, stringPart);
            il.Emit(OpCodes.Stelem_Ref);
        }

        protected virtual void CreateTokenPart(ILGenerator il, int index, string tokenPart)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_S, index);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, tokenPart);
            il.Emit(OpCodes.Callvirt, DictionaryGetMethod);
            il.Emit(OpCodes.Stelem_Ref);
        }

        protected virtual DynamicMethod GetRenderMethod(string template)
        {
            var tokens = GetTokenIndices(template, _tokens)
                .OrderBy(t => t.Key)
                .ToArray();

            var partsCount = (tokens.Length*2) + 1;
            var dmName = string.Format("Replace{0}", Guid.NewGuid().ToString("N"));

            var dm = new DynamicMethod(
                dmName,
                typeof(string),
                new [] { DictionaryType },
                typeof(TokenListParser).Module);

            var il = dm.GetILGenerator();
            var resultArray = il.DeclareLocal(typeof(string[]));
            var result = il.DeclareLocal(typeof(string));

            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ldc_I4_S, partsCount);
            il.Emit(OpCodes.Newarr, typeof(string));

            int charIndex = 0, stringArrayIndex = 0;
            for(var i = 0; i < tokens.Length; i++)
            {
                if (charIndex > tokens[i].Key)
                {
                    if (i > 0)
                    {
                        throw new TokenOverlapException(
                            tokens[i - 1].Key,
                            tokens[i - 1].Value,
                            tokens[i].Key,
                            tokens[i].Value);
                    }

                    throw new InvalidOperationException("Current char index cannot be larger than first token's char index.");
                }

                CreateStringPart(il, stringArrayIndex++, template.Substring(charIndex, tokens[i].Key - charIndex));
                CreateTokenPart(il, stringArrayIndex++, tokens[i].Value);

                charIndex = tokens[i].Key + tokens[i].Value.Length;
            }

            // assumes we always start and end on a string, possibly an empty string
            CreateStringPart(il, stringArrayIndex, template.Substring(charIndex));

            il.Emit(OpCodes.Stloc, resultArray);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldloc, resultArray);
            il.Emit(OpCodes.Call, StringJoinMethod);

            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);

            return dm;
        }
    }
}
