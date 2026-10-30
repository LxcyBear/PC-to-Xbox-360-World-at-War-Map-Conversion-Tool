        // Diagnostic-only PC OAT loader observation. Injected in the pinned
        // XBlockInputStream class. The original FF remains read-only.
        ~XBlockInputStream() override
        {
            const char* requested = std::getenv("WAW360_WEAPON_POINTER_WITNESS");
            if (!requested || !*requested || m_pointer_byte_count != 4)
                return;
            std::stringstream tokens(requested);
            std::string token;
            std::unordered_set<uint32_t> seen;
            while (std::getline(tokens, token, ','))
            {
                if (token.size() != 8)
                    continue;
                char* end = nullptr;
                const auto parsed = std::strtoul(token.c_str(), &end, 16);
                if (!end || *end || parsed == 0 || parsed >= 0xFFFFFFFEu)
                    continue;
                const auto raw = static_cast<uint32_t>(parsed);
                if (!seen.insert(raw).second)
                    continue;
                // T4 32-bit packed pointers: three upper block bits and a
                // biased low 29-bit VIRTUAL offset. Same arithmetic as
                // ConvertOffsetToPointerNative, not a source file offset.
                const auto unbiased = static_cast<uintptr_t>(raw) - 1u;
                const auto blockNum = static_cast<size_t>((unbiased & m_block_mask) >> m_block_shift);
                const auto blockOffset = static_cast<size_t>(unbiased & m_offset_mask);
                std::string status = "OUT_OF_BLOCK";
                std::string value;
                if (blockNum < m_blocks.size() && m_blocks[blockNum])
                {
                    const auto* block = m_blocks[blockNum];
                    const auto loaded = m_block_offsets[blockNum];
                    if (blockOffset < block->m_buffer_size && blockOffset < loaded)
                    {
                        const auto limit = std::min(
                            static_cast<size_t>(96),
                            std::min(block->m_buffer_size - blockOffset, loaded - blockOffset));
                        const auto* chars = block->m_buffer.get() + blockOffset;
                        bool ended = false;
                        bool safe = true;
                        for (size_t i = 0; i < limit; ++i)
                        {
                            const unsigned c = chars[i];
                            if (c == 0)
                            {
                                ended = true;
                                break;
                            }
                            if (!((c >= 'a' && c <= 'z') ||
                                  (c >= 'A' && c <= 'Z') ||
                                  (c >= '0' && c <= '9') ||
                                  c == '_' || c == '-' || c == '.' || c == '/'))
                            {
                                safe = false;
                                break;
                            }
                            value += static_cast<char>(c);
                        }
                        status = safe && ended && !value.empty()
                            ? "STRING_AT_DIRECT_VIRTUAL_TARGET"
                            : "NOT_VALID_NAME_STRING";
                    }
                    else
                        status = "TARGET_NOT_LOADED";
                }
                std::cerr << "[WAW360-WPTR] raw=" << token
                          << " block=" << blockNum
                          << " offset=0x" << std::hex << blockOffset << std::dec
                          << " status=" << status;
                if (status == "STRING_AT_DIRECT_VIRTUAL_TARGET")
                    std::cerr << " name=" << value;
                std::cerr << std::endl;
            }
        }
