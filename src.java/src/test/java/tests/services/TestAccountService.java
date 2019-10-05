package tests.services;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import javax.servlet.http.HttpServletRequest;

import org.apache.commons.lang3.RandomStringUtils;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.mockito.Mockito;

import com.ackdev.childDev.service.AccountService;
import com.common.security.dto.RegisterUserDTO;
import com.common.security.jdo.UserAccountV2;

import testUtils.DataStoreTestHelper;
import testUtils.Helpers;

public class TestAccountService extends Mockito {
	private AccountService mAccountService = new AccountService();

	@Before
	public void init() {
		DataStoreTestHelper.setupObjectify();
	}

	@After
	public void tearDown() {
		System.out.println("tearDown");
//		helper.tearDown();
		DataStoreTestHelper.closeObjectify();
	}

	@Test
	public void testSignup() {
		HttpServletRequest request = mock(HttpServletRequest.class);
		RegisterUserDTO pRegisterUserDTO = Helpers.makeUserReg();
		UserAccountV2 ua = mAccountService.register(pRegisterUserDTO, request);
		assertTrue(ua.authHash.length() > 10);
		UserAccountV2 userAccount = mAccountService.getToken(pRegisterUserDTO.nickName, pRegisterUserDTO.password,
				request);
		assertTrue(userAccount.authHash.length() > 10);
		assertTrue(userAccount.nickName.equals(pRegisterUserDTO.nickName));
		assertFalse(mAccountService.checkNameAvailable(userAccount.nickName).isSuccess());
		assertTrue(mAccountService.checkNameAvailable(userAccount.nickName+"d").isSuccess());
	}

	@Test
	public void testSpoof() {
		HttpServletRequest request = mock(HttpServletRequest.class);
		RegisterUserDTO pRegisterUserDTO = Helpers.makeUserReg();
		UserAccountV2 ua = mAccountService.register(pRegisterUserDTO, request);

		assertTrue(ua.authHash.length() > 10);
		UserAccountV2 userAccount = mAccountService.getToken(pRegisterUserDTO.nickName, pRegisterUserDTO.password,
				request);
		assertTrue(userAccount.authHash.length() > 10);
		UserAccountV2 userAccountFail = mAccountService.getToken(pRegisterUserDTO.nickName,
				pRegisterUserDTO.password + "f", request);
		assertTrue(userAccountFail == null);
		UserAccountV2 userAccountFail2 = mAccountService.getToken(pRegisterUserDTO.nickName + "f",
				pRegisterUserDTO.password, request);
		assertTrue(userAccountFail2 == null);

	}

}
