        // Diagnostic-only PC OAT loader observation. Injected in the pinned
        // XBlockInputStream class. The original FF remains read-only.
        ~XBlockInputStream() override
        {
            const char* requested = std::getenv("WAW360_WEAPON_POINTER_WITNESS");
            const char* flameRequested = std::getenv("WAW360_FLAME_POINTER_WITNESS");
            if (m_pointer_byte_count != 4)
                return;
            // Source-only packed VIRTUAL witness: this is the original PC
            // OAT loader's actual allocated block, NOT a decompressed-FF
            // physical offset. Never write the source FF or produce a donor.
            if (flameRequested && *flameRequested)
            {
                // Typed read-only source provenance: every C-string and
                // 112B Material is accessed ONLY if the actual pointer lies
                // inside an OAT-loaded x86 block. No arbitrary dereference.
                const auto loadedBytes = [&](const uint32_t address, size_t count)
                    -> const unsigned char*
                {
                    if (!address || !count) return nullptr;
                    for (size_t bi=0;bi<m_blocks.size();++bi)
                    {
                        const auto* b=m_blocks[bi];
                        if(!b || !b->m_buffer)continue;
                        const auto start=reinterpret_cast<uintptr_t>(b->m_buffer.get());
                        const auto loaded=std::min(b->m_buffer_size,m_block_offsets[bi]);
                        const auto addr=static_cast<uintptr_t>(address);
                        if(addr>=start && addr-start<=loaded &&
                           count<=loaded-(addr-start))
                            return reinterpret_cast<const unsigned char*>(b->m_buffer.get())+(addr-start);
                    }
                    return nullptr;
                };
                const auto read32 = [](const unsigned char* p)->uint32_t {
                    return static_cast<uint32_t>(p[0]) |
                        (static_cast<uint32_t>(p[1])<<8) |
                        (static_cast<uint32_t>(p[2])<<16) |
                        (static_cast<uint32_t>(p[3])<<24);
                };
                const auto loadedString = [&](uint32_t ptr)->std::string {
                    if(!ptr)return {};
                    // Require an owned NUL before the known loaded extent.
                    std::string value;
                    for(size_t i=0;i<96;i++)
                    {
                        const auto* ch=loadedBytes(ptr+static_cast<uint32_t>(i),1);
                        if(!ch)return {};
                        if(*ch==0)return value.empty()?std::string{}:value;
                        if(!((*ch>='a'&&*ch<='z')||(*ch>='A'&&*ch<='Z')||
                             (*ch>='0'&&*ch<='9')||*ch=='_'||*ch=='-'||
                             *ch=='.'||*ch=='/'))
                            return {};
                        value.push_back(static_cast<char>(*ch));
                    }
                    return {};
                };
                std::stringstream flameTokens(flameRequested);
                std::string flameToken;
                std::unordered_set<uint32_t> flameSeen;
                while (std::getline(flameTokens, flameToken, ','))
                {
                    if (flameToken.size() != 8)
                        continue;
                    char* end = nullptr;
                    const auto parsed = std::strtoul(flameToken.c_str(), &end, 16);
                    if (!end || *end || parsed == 0 || parsed >= 0xFFFFFFFEu)
                        continue;
                    const auto raw = static_cast<uint32_t>(parsed);
                    if (!flameSeen.insert(raw).second)
                        continue;
                    const auto unbiased = static_cast<uintptr_t>(raw) - 1u;
                    const auto blockNum = static_cast<size_t>(
                        (unbiased & m_block_mask) >> m_block_shift);
                    const auto blockOffset = static_cast<size_t>(
                        unbiased & m_offset_mask);
                    std::string status = "BLOCK_NOT_ALLOCATED";
                    std::string asciiName;
                    std::string headerHex;
                    const unsigned char* typed=nullptr;
                    size_t typedRemaining=0;
                    if (blockNum < m_blocks.size() && m_blocks[blockNum])
                    {
                        const auto* block = m_blocks[blockNum];
                        const auto loaded = m_block_offsets[blockNum];
                        if (blockOffset < block->m_buffer_size && blockOffset < loaded)
                        {
                            const auto remaining = std::min(
                                block->m_buffer_size-blockOffset,
                                loaded-blockOffset);
                            const auto* bytes = reinterpret_cast<const unsigned char*>(
                                block->m_buffer.get()+blockOffset);
                            typed=bytes;
                            typedRemaining=remaining;
                            const auto sample = std::min(static_cast<size_t>(32),remaining);
                            const char hexDigits[]="0123456789ABCDEF";
                            for(size_t i=0;i<sample;i++)
                            {
                                headerHex.push_back(hexDigits[(bytes[i]>>4)&15]);
                                headerHex.push_back(hexDigits[bytes[i]&15]);
                            }
                            status="LOADED_RAW_TARGET_BYTES_UNTYPED";
                            bool clean=true;
                            bool ended=false;
                            for(size_t i=0;i<std::min(static_cast<size_t>(96),remaining);i++)
                            {
                                const auto ch=bytes[i];
                                if(ch==0){ended=true;break;}
                                if (!((ch>='a' && ch<='z') ||
                                      (ch>='A' && ch<='Z') ||
                                      (ch>='0' && ch<='9') ||
                                      ch=='_' || ch=='-' || ch=='.' || ch=='/'))
                                {clean=false;break;}
                                asciiName.push_back(static_cast<char>(ch));
                            }
                            if(clean && ended && !asciiName.empty())
                                status="LOADED_DIRECT_STRING_TARGET";
                            else asciiName.clear();
                        }
                        else status="TARGET_NOT_LOADED";
                    }
                    std::cerr << "[WAW360-FLAMEPTR] raw=" << flameToken
                              << " block=" << blockNum
                              << " offset=0x" << std::hex << blockOffset << std::dec
                              << " status=" << status
                              << " header32=" << headerHex;
                    if(!asciiName.empty()) std::cerr << " name=" << asciiName;
                    std::cerr << std::endl;
                    // Independent byte-level typed projection. Do not claim
                    // runtime validity merely because a plausible source
                    // structure is resident in OAT loaded memory.
                    std::string materialName;
                    std::string techniqueName;
                    std::string aliasName;
                    std::string imageNames;
                    unsigned textures=0,constants=0,stateBits=0;
                    unsigned imageNamesRead=0;
                    bool materialShape=false;
                    if(typed && typedRemaining>=112)
                    {
                        const auto name=read32(typed);
                        textures=typed[91];
                        constants=typed[92];
                        stateBits=typed[93];
                        if(textures<=64 && constants<=64 && stateBits<=64)
                        {
                            materialName=loadedString(name);
                            materialShape=!materialName.empty();
                            if(materialShape)
                            {
                                const auto tech=read32(typed+96);
                                const auto* techHeader=loadedBytes(tech,4);
                                if(techHeader)
                                    techniqueName=loadedString(read32(techHeader));
                                const auto tex=read32(typed+100);
                                if(textures==0 || loadedBytes(tex,static_cast<size_t>(textures)*16))
                                {
                                    for(unsigned ix=0;ix<textures;ix++)
                                    {
                                        const auto* table=loadedBytes(tex+ix*16,16);
                                        if(!table)break;
                                        const auto* img=loadedBytes(read32(table+12),36);
                                        // GfxImage.name is the 32-bit cell at +32.
                                        const auto imageName=img?loadedString(read32(img+32)):std::string{};
                                        if(!imageNames.empty())imageNames.push_back(';');
                                        imageNames+=imageName.empty()?"?":imageName;
                                        if(!imageName.empty())imageNamesRead++;
                                    }
                                }
                            }
                        }
                    }
                    // SndAliasCustom on T4 is precisely one pointer-sized
                    // record to a real loaded alias name, NOT an audio sample.
                    if(typed && typedRemaining>=4)
                        aliasName=loadedString(read32(typed));
                    std::cerr << "[WAW360-FLAMETYPED] raw=" << flameToken
                              << " shape=" << (materialShape?"PC_MATERIAL112_NAME_MATCH":"NO_TYPED_MATERIAL_MATCH")
                              << " materialName=" << (materialName.empty()?"-":materialName)
                              << " techniqueName=" << (techniqueName.empty()?"-":techniqueName)
                              << " textureCount=" << textures
                              << " constantCount=" << constants
                              << " stateBitsCount=" << stateBits
                              << " namedImages=" << imageNamesRead
                              << " images=" << (imageNames.empty()?"-":imageNames)
                              << " sndAliasName=" << (aliasName.empty()?"-":aliasName)
                              << std::endl;
                }
            }
            if (!requested || !*requested)
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
